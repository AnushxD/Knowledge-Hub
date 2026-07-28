using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

internal sealed class ChunkRepository(DocHubDbContext db) : IChunkRepository
{
    public async Task ReplaceAsync(
        Guid documentId,
        int documentVersion,
        IReadOnlyList<NewChunkDto> chunks,
        CancellationToken ct = default)
    {
        // The delete and the insert have to land together: a crash between them
        // would leave an Indexed document with nothing to retrieve, which looks
        // to a user exactly like the document silently losing its content.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId)
            .ExecuteDeleteAsync(ct);

        var now = DateTimeOffset.UtcNow;

        db.DocumentChunks.AddRange(chunks.Select(chunk => new DocumentChunk
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            Ordinal = chunk.Ordinal,
            Text = chunk.Text,
            SectionRef = chunk.SectionRef,
            TokenCount = chunk.TokenCount,
            DocumentVersion = documentVersion,
            Embedding = new Vector(chunk.Embedding),
            CreatedAt = now,
        }));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public Task<int> DeleteForDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        db.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId)
            .ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<ChunkMatchDto>> GetForDocumentAsync(
        Guid documentId,
        CancellationToken ct = default) =>
        await db.DocumentChunks
            .AsNoTracking()
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.Ordinal)
            .Select(chunk => new ChunkMatchDto(
                chunk.Id,
                chunk.DocumentId,
                chunk.Document!.Title,
                chunk.Document.FileName,
                chunk.Document.Extension,
                chunk.Document.FolderId,
                chunk.Document.Folder!.Path,
                chunk.Ordinal,
                chunk.SectionRef,
                chunk.Text,
                0))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ChunkMatchDto>> SearchKeywordAsync(
        ChunkSearchDto query,
        CancellationToken ct = default)
    {
        var candidates = await ApplyFiltersAsync(query, ct);
        if (candidates is null) return [];

        // websearch_to_tsquery rather than to_tsquery: it accepts whatever the
        // user typed — quotes, OR, stray punctuation — without ever throwing a
        // syntax error back at them.
        //
        // Repeated inline rather than hoisted into a local: EF only translates
        // EF.Functions calls that appear inside the expression tree, and a
        // hoisted one throws at run time asking to be rewritten like this.
        const string configuration = DocHubDbContext.SearchConfiguration;
        var text = query.Text;

        return await candidates
            .Where(chunk => chunk.SearchVector!.Matches(
                EF.Functions.WebSearchToTsQuery(configuration, text)))
            .OrderByDescending(chunk => chunk.SearchVector!.Rank(
                EF.Functions.WebSearchToTsQuery(configuration, text)))
            .Take(query.Limit)
            .Select(chunk => new ChunkMatchDto(
                chunk.Id,
                chunk.DocumentId,
                chunk.Document!.Title,
                chunk.Document.FileName,
                chunk.Document.Extension,
                chunk.Document.FolderId,
                chunk.Document.Folder!.Path,
                chunk.Ordinal,
                chunk.SectionRef,
                chunk.Text,
                chunk.SearchVector!.Rank(
                    EF.Functions.WebSearchToTsQuery(configuration, text))))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChunkMatchDto>> SearchVectorAsync(
        ChunkSearchDto query,
        float[] queryEmbedding,
        CancellationToken ct = default)
    {
        var candidates = await ApplyFiltersAsync(query, ct);
        if (candidates is null) return [];

        var vector = new Vector(queryEmbedding);

        // Ordering by cosine distance is what lets Postgres use the HNSW index;
        // the similarity in the projection is only for display and does not
        // affect the plan.
        return await candidates
            .OrderBy(chunk => chunk.Embedding.CosineDistance(vector))
            .Take(query.Limit)
            .Select(chunk => new ChunkMatchDto(
                chunk.Id,
                chunk.DocumentId,
                chunk.Document!.Title,
                chunk.Document.FileName,
                chunk.Document.Extension,
                chunk.Document.FolderId,
                chunk.Document.Folder!.Path,
                chunk.Ordinal,
                chunk.SectionRef,
                chunk.Text,
                1 - chunk.Embedding.CosineDistance(vector)))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The filter both branches share. Returns null when a filter cannot match
    /// anything — an unknown folder id — so callers skip the query entirely
    /// rather than running one guaranteed to return nothing.
    /// </summary>
    private async Task<IQueryable<DocumentChunk>?> ApplyFiltersAsync(
        ChunkSearchDto query,
        CancellationToken ct)
    {
        // Retrieval is restricted to Indexed documents. Anything still in the
        // pipeline, or that failed it, must not be findable or citable.
        var chunks = db.DocumentChunks
            .AsNoTracking()
            .Where(chunk => chunk.Document!.Status == IngestionStatus.Indexed);

        if (query.FolderId is { } folderId)
        {
            var path = await db.Folders
                .Where(folder => folder.Id == folderId)
                .Select(folder => folder.Path)
                .FirstOrDefaultAsync(ct);

            if (path is null) return null;

            chunks = chunks.Where(chunk =>
                chunk.Document!.Folder!.Path == path ||
                EF.Functions.Like(chunk.Document!.Folder!.Path, path + "/%"));
        }

        if (query.OwnerId is { } ownerId)
            chunks = chunks.Where(chunk => chunk.Document!.OwnerId == ownerId);

        if (query.Extensions is { Count: > 0 } extensions)
            chunks = chunks.Where(chunk => extensions.Contains(chunk.Document!.Extension));

        if (query.Tags is { Count: > 0 } tags)
            chunks = chunks.Where(chunk => chunk.Document!.Tags.Any(tag => tags.Contains(tag)));

        return chunks;
    }
}
