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

        if (query.OwnerId is { } ownerId)
            documents = documents.Where(document => document.OwnerId == ownerId);

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
            // Case-insensitive LIKE. This is plain substring matching, not the
            // real thing — hybrid keyword + vector search arrives in phase 2.
            documents = documents.Where(document =>
                EF.Functions.ILike(document.Title, text) ||
                EF.Functions.ILike(document.FileName, text) ||
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
        var location = await db.Documents
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new { candidate.StoragePath, FolderPath = candidate.Folder!.Path })
            .FirstOrDefaultAsync(ct);

        if (location is null) return null;

        var dto = await db.Documents
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(Projection)
            .FirstAsync(ct);

        var folderPath = location.FolderPath;
        var breadcrumb = await db.Folders
            .AsNoTracking()
            .Where(folder =>
                folder.Path == folderPath ||
                EF.Functions.Like(folderPath, folder.Path + "/%"))
            .OrderBy(folder => folder.Path.Length)
            .Select(folder => new FolderDto(
                folder.Id, folder.ParentId, folder.Name, folder.Path,
                folder.OwnerId, 0, folder.CreatedAt, folder.UpdatedAt))
            .ToListAsync(ct);

        var versions = await db.DocumentVersions
            .AsNoTracking()
            .Where(version => version.DocumentId == id)
            .OrderByDescending(version => version.VersionNumber)
            .Select(version => new DocumentVersionDto(
                version.VersionNumber,
                version.StoragePath,
                version.SizeBytes,
                version.Note,
                new UserDto(
                    version.ChangedBy!.Id,
                    version.ChangedBy.Name,
                    // Nullable on IdentityUser, required by our column — the
                    // fallback satisfies the compiler and can never be hit.
                    version.ChangedBy.Email ?? string.Empty,
                    version.ChangedBy.Role),
                version.ChangedAt))
            .ToListAsync(ct);

        return new DocumentDetailDto(dto, location.StoragePath, breadcrumb, versions);
    }

    public Task<string?> GetStoragePathAsync(Guid id, CancellationToken ct = default) =>
        db.Documents
            .Where(document => document.Id == id)
            .Select(document => document.StoragePath)
            .FirstOrDefaultAsync(ct);

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
            Description = input.Description,
            FileName = input.FileName,
            Extension = input.Extension,
            ContentType = input.ContentType,
            SizeBytes = input.SizeBytes,
            StoragePath = input.StoragePath,
            Version = 1,
            Tags = [.. input.Tags],
            OwnerId = input.OwnerId,
            Status = IngestionStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Version 1 is written alongside the document, so history is complete
        // from the first upload rather than starting at the first edit.
        document.Versions.Add(new DocumentVersion
        {
            Id = Guid.CreateVersion7(),
            DocumentId = document.Id,
            VersionNumber = 1,
            StoragePath = input.StoragePath,
            SizeBytes = input.SizeBytes,
            Note = "Initial upload",
            ChangedById = input.OwnerId,
            ChangedAt = now,
        });

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        return await RequireDtoAsync(document.Id, ct);
    }

    public async Task<DocumentDto?> AddVersionAsync(
        Guid id,
        string storagePath,
        long sizeBytes,
        string? note,
        Guid changedById,
        CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (document is null) return null;

        var now = DateTimeOffset.UtcNow;
        document.Version += 1;
        document.StoragePath = storagePath;
        document.SizeBytes = sizeBytes;
        document.UpdatedAt = now;

        // New content means the old chunks are stale, so the document drops
        // back to Pending and is re-ingested.
        document.Status = IngestionStatus.Pending;
        document.FailureReason = null;
        document.ChunkCount = null;

        db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.CreateVersion7(),
            DocumentId = document.Id,
            VersionNumber = document.Version,
            StoragePath = storagePath,
            SizeBytes = sizeBytes,
            Note = note,
            ChangedById = changedById,
            ChangedAt = now,
        });

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(id, ct);
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
        CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (document is null) return null;

        document.Status = status;
        document.FailureReason = status == IngestionStatus.Failed ? failureReason : null;
        // A chunk count only means anything once ingestion has finished.
        document.ChunkCount = status == IngestionStatus.Indexed ? chunkCount : null;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(id, ct);
    }

    public async Task<DocumentDto?> MoveAsync(Guid id, Guid folderId, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (document is null) return null;

        document.FolderId = folderId;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await RequireDtoAsync(id, ct);
    }

    public async Task<IReadOnlyList<string>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (document is null) return [];

        var blobs = await db.DocumentVersions
            .Where(version => version.DocumentId == id)
            .Select(version => version.StoragePath)
            .ToListAsync(ct);

        blobs.Add(document.StoragePath);

        db.Documents.Remove(document);
        await db.SaveChangesAsync(ct);

        return blobs.Distinct().ToList();
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
            StorageBytes: byStatus.Sum(row => row.Bytes),
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

    public async Task<IReadOnlyList<UserDto>> GetOwnersAsync(CancellationToken ct = default) =>
        await db.Documents
            .AsNoTracking()
            .Select(document => document.Owner!)
            .Distinct()
            .OrderBy(owner => owner.Name)
            .Select(owner => new UserDto(
                owner.Id, owner.Name, owner.Email ?? string.Empty, owner.Role))
            .ToListAsync(ct);

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
            document.Version,
            document.Tags,
            new UserDto(
                document.Owner!.Id,
                document.Owner.Name,
                document.Owner.Email ?? string.Empty,
                document.Owner.Role),
            document.Status,
            document.FailureReason,
            document.ChunkCount,
            document.IsStarred,
            document.CreatedAt,
            document.UpdatedAt);
}
