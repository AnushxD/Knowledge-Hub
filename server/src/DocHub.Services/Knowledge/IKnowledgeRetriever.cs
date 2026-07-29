using DocHub.Services.Search;
using DocHub.Services.ViewModels;

namespace DocHub.Services.Knowledge;

/// <summary>
/// Everything the assistant may ground an answer in, as one thing.
///
/// The RAG orchestrator depends on this rather than on
/// <see cref="ISearchService"/> so that adding a body of knowledge — source
/// repositories over MCP, an issue tracker, a wiki — is a registration, not a
/// change to how answers are produced.
///
/// It deliberately returns <see cref="RetrievedPassage"/>, the type the prompt
/// builder and citation verifier already speak. A source is free to be shaped
/// nothing like a document internally; by the time the orchestrator sees it, it
/// is a passage that can be cited.
/// </summary>
public interface IKnowledgeRetriever
{
    /// <summary>
    /// Searches every configured source and returns the best passages across
    /// all of them.
    /// </summary>
    Task<GroundingResult> RetrieveAsync(
        SearchRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Each source and its current state, for the sources screen. Reports
    /// rather than searches — a screen that had to ask a question to draw
    /// itself would be both slow and misleading.
    /// </summary>
    Task<IReadOnlyList<KnowledgeSourceViewModel>> DescribeSourcesAsync(
        CancellationToken ct = default);
}

/// <summary>Passages gathered from every source, ready to ground an answer.</summary>
/// <param name="Passages">Best first, already merged across sources.</param>
/// <param name="Degradations">
/// One sentence per source that answered with less than it should have, or did
/// not answer at all. Empty on a healthy retrieval. The answer still goes
/// ahead — but a thin answer and a broken source must not look the same to the
/// person reading it.
/// </param>
public sealed record GroundingResult(
    IReadOnlyList<RetrievedPassage> Passages,
    IReadOnlyList<string> Degradations);
