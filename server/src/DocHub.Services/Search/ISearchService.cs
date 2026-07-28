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
}
