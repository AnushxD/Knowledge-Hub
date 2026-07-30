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

/// <summary>Where a retrieved passage lives. Mirrors the Integrations enum.</summary>
public enum PassageKind
{
    /// <summary>A document in this hub, addressable by id and chunk.</summary>
    Document = 0,

    /// <summary>Something outside the hub, such as a repository file.</summary>
    External = 1,
}

/// <summary>
/// One passage offered to the model as grounding.
///
/// Repeated rather than reusing the Integrations record, matching how every
/// other contract crosses this boundary: Services owns its own shapes so a
/// change to an external contract cannot ripple into the orchestrator by
/// accident.
/// </summary>
/// <param name="Text">The chunk in full, not a snippet.</param>
/// <param name="DocumentId">Set for <see cref="PassageKind.Document"/> only.</param>
/// <param name="Url">A link for an external passage, when one exists.</param>
/// <param name="SourceName">
/// Which knowledge source produced this, carried through so it can be persisted
/// with the citation.
/// </param>
public sealed record RetrievedPassage(
    PassageKind Kind,
    string Title,
    int ChunkId,
    string Heading,
    string Text,
    double Score,
    string MatchedBy,
    Guid? DocumentId = null,
    string? Url = null,
    string? SourceName = null);

/// <param name="VectorSearchError">
/// Set when the vector branch was unavailable. Retrieval still returns keyword
/// results, but the caller should know the grounding is thinner than usual.
/// </param>
public sealed record RetrievalResult(
    IReadOnlyList<RetrievedPassage> Passages,
    string? VectorSearchError);
