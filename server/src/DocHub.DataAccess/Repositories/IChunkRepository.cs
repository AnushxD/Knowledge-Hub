using DocHub.DataAccess.Dtos;

namespace DocHub.DataAccess.Repositories;

/// <summary>
/// Persistence for the retrievable passages of a document, and the two search
/// branches over them.
///
/// The branches stay separate on purpose: each is a query Postgres can plan
/// well on its own index, and merging them is a ranking decision that belongs
/// to the Service layer, not to SQL.
/// </summary>
public interface IChunkRepository
{
    /// <summary>
    /// Makes <paramref name="chunks"/> the complete set for a document,
    /// discarding whatever was there before. Re-ingesting is therefore
    /// idempotent — a retried job cannot leave duplicates behind.
    /// </summary>
    Task ReplaceAsync(
        Guid documentId,
        int documentVersion,
        IReadOnlyList<NewChunkDto> chunks,
        CancellationToken ct = default);

    /// <summary>Drops every chunk for a document. Returns how many were removed.</summary>
    Task<int> DeleteForDocumentAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Chunks of one document in reading order, for previews and citations.</summary>
    Task<IReadOnlyList<ChunkMatchDto>> GetForDocumentAsync(
        Guid documentId,
        CancellationToken ct = default);

    /// <summary>Full-text branch: Postgres tsvector ranked by ts_rank, best first.</summary>
    Task<IReadOnlyList<ChunkMatchDto>> SearchKeywordAsync(
        ChunkSearchDto query,
        CancellationToken ct = default);

    /// <summary>Vector branch: pgvector cosine nearest neighbours, closest first.</summary>
    Task<IReadOnlyList<ChunkMatchDto>> SearchVectorAsync(
        ChunkSearchDto query,
        float[] queryEmbedding,
        CancellationToken ct = default);
}
