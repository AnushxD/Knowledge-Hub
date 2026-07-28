using DocHub.Services.ViewModels;

namespace DocHub.Services.Search;

/// <summary>
/// Hybrid search over indexed document chunks.
///
/// Runs a keyword branch and a vector branch and fuses them, because neither
/// alone is enough: keyword search misses a question phrased in different words
/// than the document, and vector search misses exact identifiers — an error
/// code, a product name — that it has no reason to consider close to anything.
/// </summary>
public interface ISearchService
{
    Task<SearchResponseViewModel> SearchAsync(
        SearchRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves passages to ground an answer on.
    ///
    /// Same ranking as <see cref="SearchAsync"/> — deliberately, so what the
    /// assistant reasons over is what the search screen would have shown — but
    /// returns each passage's full text instead of a display snippet. A model
    /// handed a truncated snippet answers from a truncated source.
    /// </summary>
    Task<RetrievalResult> RetrieveAsync(
        SearchRequest request,
        CancellationToken ct = default);
}

/// <summary>One passage offered to the model as grounding.</summary>
/// <param name="Text">The chunk in full, not a snippet.</param>
public sealed record RetrievedPassage(
    Guid DocumentId,
    string DocumentTitle,
    int ChunkId,
    string Heading,
    string Text,
    double Score,
    string MatchedBy);

/// <param name="VectorSearchError">
/// Set when the vector branch was unavailable. Retrieval still returns keyword
/// results, but the caller should know the grounding is thinner than usual.
/// </param>
public sealed record RetrievalResult(
    IReadOnlyList<RetrievedPassage> Passages,
    string? VectorSearchError);
