using System.Linq.Expressions;
using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

internal sealed class DocumentRepository(DocHubDbContext db) : IDocumentRepository
{
    public async Task<IReadOnlyList<DocumentDto>> QueryAsync(
        DocumentQueryDto query,
        CancellationToken ct = default)
    {
        var documents = db.Documents.AsNoTracking().AsQueryable();

        if (query.FolderId is { } folderId)
        {
            if (query.Recursive)
            {
                var path = await db.Folders
                    .Where(folder => folder.Id == folderId)
                    .Select(folder => folder.Path)
                    .FirstOrDefaultAsync(ct);

                if (path is null) return [];

                documents = documents.Where(document =>
                    document.Folder!.Path == path ||
                    EF.Functions.Like(document.Folder!.Path, path + "/%"));
            }
            else
            {
                documents = documents.Where(document => document.FolderId == folderId);
            }
        }

        if (query.StarredOnly)
            documents = documents.Where(document => document.IsStarred);

        if (query.Statuses is { Count: > 0 } statuses)
            documents = documents.Where(document => statuses.Contains(document.Status));

        if (query.Extensions is { Count: > 0 } extensions)
            documents = documents.Where(document => extensions.Contains(document.Extension));

        // Overlap on text[] — Postgres answers this from the GIN index.
        if (query.Tags is { Count: > 0 } tags)
            documents = documents.Where(document => document.Tags.Any(tag => tags.Contains(tag)));

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = $"%{query.Text.Trim()}%";
            // Plain substring matching over names and metadata, which is all
            // this listing is for. Content is searched properly on the search
            // screen, through the hybrid index.
            documents = documents.Where(document =>
                EF.Functions.ILike(document.Title, text) ||
                EF.Functions.ILike(document.FileName, text) ||
                EF.Functions.ILike(document.RepositoryPath, text) ||
                (document.Description != null && EF.Functions.ILike(document.Description, text)));
        }

        documents = query.Sort switch
        {
            DocumentSort.UpdatedAscending => documents.OrderBy(document => document.UpdatedAt),
            DocumentSort.NameAscending => documents.OrderBy(document => document.Title),
            DocumentSort.NameDescending => documents.OrderByDescending(document => document.Title),
            DocumentSort.SizeDescending => documents.OrderByDescending(document => document.SizeBytes),
            _ => documents.OrderByDescending(document => document.UpdatedAt),
        };

        return await documents
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(Projection)
            .ToListAsync(ct);
    }

    public async Task<DocumentDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var folderPath = await db.Documents
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => candidate.Folder!.Path)
            .FirstOrDefaultAsync(ct);

        if (folderPath is null) return null;

        var dto = await db.Documents
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(Projection)
            .FirstAsync(ct);

        var breadcrumb = await db.Folders
            .AsNoTracking()
            .Where(folder =>
                folder.Path == folderPath ||
                EF.Functions.Like(folderPath, folder.Path + "/%"))
            .OrderBy(folder => folder.Path.Length)
            .Select(folder => new FolderDto(
                folder.Id, folder.ParentId, folder.Name, folder.Path,
                0, folder.CreatedAt, folder.UpdatedAt))
            .ToListAsync(ct);

        return new DocumentDetailDto(dto, breadcrumb);
    }

    public Task<string?> GetRepositoryPathAsync(Guid id, CancellationToken ct = default) =>
        db.Documents
            .Where(document => document.Id == id)
            .Select(document => document.RepositoryPath)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<MirroredFileDto>> GetMirrorAsync(CancellationToken ct = default) =>
        await db.Documents
            .AsNoTracking()
            .Select(document => new MirroredFileDto(
                document.Id, document.RepositoryPath, document.BlobSha, document.Title,
                document.Status))
            .ToListAsync(ct);

    public async Task<DocumentDto> CreateAsync(
        NewDocumentDto input,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            FolderId = input.FolderId,
            Title = input.Title,
            FileName = input.FileName,
            Extension = input.Extension,
            ContentType = input.ContentType,
            RepositoryPath = input.RepositoryPath,
            BlobSha = input.BlobSha,
            CommitSha = input.CommitSha,
            // Unknown until the file is fetched; ingestion fills it in.
            SizeBytes = 0,
            Status = IngestionStatus.Pending,
            LastSyncedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        return await RequireDtoAsync(document.Id, ct);
    }

    public async Task<DocumentDto?> SetContentAsync(
        Guid id,
        string blobSha,
        string? commitSha,
        CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (document is null) return null;

        var now = DateTimeOffset.UtcNow;
        document.BlobSha = blobSha;
        document.CommitSha = commitSha;
        document.LastSyncedAt = now;
        document.UpdatedAt = now;

        // New content means the old chunks are stale, so the document drops
        // back to Pending and is re-ingested. The size goes with them: it
        // described the previous revision.
        document.Status = IngestionStatus.Pending;
        document.FailureReason = null;
        document.ChunkCount = null;
        document.SizeBytes = 0;

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(id, ct);
    }

    public async Task TouchAsync(
        IReadOnlyList<Guid> ids,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        if (ids.Count == 0) return;

        // One UPDATE rather than loading a repository's worth of entities to
        // set a single timestamp on each. UpdatedAt is deliberately untouched:
        // nothing about the document changed, only when it was last looked at.
        await db.Documents
            .Where(document => ids.Contains(document.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(document => document.LastSyncedAt, at),
                ct);
    }

    public async Task<DocumentDto?> UpdateMetadataAsync(
        Guid id,
        DocumentMetadataUpdateDto update,
        CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (document is null) return null;

        if (update.Title is not null) document.Title = update.Title;
        if (update.Description is not null) document.Description = update.Description;
        if (update.Tags is not null) document.Tags = [.. update.Tags];
        if (update.IsStarred is { } starred) document.IsStarred = starred;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(id, ct);
    }

    public async Task<DocumentDto?> SetStatusAsync(
        Guid id,
        IngestionStatus status,
        string? failureReason = null,
        int? chunkCount = null,
        long? sizeBytes = null,
        CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (document is null) return null;

        document.Status = status;

        // Clamped here rather than at the caller: this runs while handling a
        // failure, and the reason often comes straight from an exception
        // message, whose length nothing upstream controls. An overflow would
        // throw out of the handler and lose the failure it was recording.
        document.FailureReason = status == IngestionStatus.Failed
            ? Truncate.ToFit(failureReason, DocHubDbContext.FailureReasonMaxLength)
            : null;
        // A chunk count only means anything once ingestion has finished.
        document.ChunkCount = status == IngestionStatus.Indexed ? chunkCount : null;
        if (sizeBytes is { } size) document.SizeBytes = size;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(id, ct);
    }

    public async Task<int> DeleteManyAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;

        // The chunks go with them by cascade, which is what keeps a file that
        // left the repository from staying answerable through search.
        var removed = await db.Documents
            .Where(document => ids.Contains(document.Id))
            .ExecuteDeleteAsync(ct);

        // ExecuteDelete goes straight to the database and leaves the change
        // tracker still believing these rows exist. The next SaveChanges on
        // this context — during one sync that is the folder reconciliation and
        // then the sync record itself — would cascade or update a row that has
        // gone, and fail the whole sync on a concurrency error. Detaching them
        // is what keeps the two views of the world agreeing.
        var doomed = ids.ToHashSet();

        foreach (var entry in db.ChangeTracker.Entries<Document>()
            .Where(entry => doomed.Contains(entry.Entity.Id))
            .ToList())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in db.ChangeTracker.Entries<DocumentChunk>()
            .Where(entry => doomed.Contains(entry.Entity.DocumentId))
            .ToList())
        {
            entry.State = EntityState.Detached;
        }

        return removed;
    }

    public async Task<LibraryStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        // One round trip rather than six counts.
        var byStatus = await db.Documents
            .GroupBy(document => document.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count(),
                Bytes = group.Sum(document => document.SizeBytes),
                Chunks = group.Sum(document => document.ChunkCount ?? 0),
            })
            .ToListAsync(ct);

        var folders = await db.Folders.CountAsync(ct);

        int CountOf(IngestionStatus status) =>
            byStatus.FirstOrDefault(row => row.Status == status)?.Count ?? 0;

        return new LibraryStatsDto(
            Documents: byStatus.Sum(row => row.Count),
            Indexed: CountOf(IngestionStatus.Indexed),
            InPipeline: CountOf(IngestionStatus.Pending) + CountOf(IngestionStatus.Indexing),
            Failed: CountOf(IngestionStatus.Failed),
            Folders: folders,
            ContentBytes: byStatus.Sum(row => row.Bytes),
            Chunks: byStatus.Sum(row => row.Chunks));
    }

    public async Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct = default)
    {
        var tagArrays = await db.Documents
            .AsNoTracking()
            .Where(document => document.Tags.Length > 0)
            .Select(document => document.Tags)
            .ToListAsync(ct);

        return tagArrays
            .SelectMany(tags => tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<DocumentDto> RequireDtoAsync(Guid id, CancellationToken ct) =>
        await db.Documents
            .AsNoTracking()
            .Where(document => document.Id == id)
            .Select(Projection)
            .FirstAsync(ct);

    /// <summary>
    /// Single projection shared by every read, so a column added to the DTO is
    /// added in exactly one place and no query silently returns stale shape.
    /// </summary>
    private static readonly Expression<Func<Document, DocumentDto>> Projection = document =>
        new DocumentDto(
            document.Id,
            document.FolderId,
            document.Title,
            document.Description,
            document.FileName,
            document.Extension,
            document.ContentType,
            document.SizeBytes,
            document.RepositoryPath,
            document.BlobSha,
            document.CommitSha,
            document.Tags,
            document.Status,
            document.FailureReason,
            document.ChunkCount,
            document.IsStarred,
            document.LastSyncedAt,
            document.CreatedAt,
            document.UpdatedAt);
}
