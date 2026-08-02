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
    /// Which sources exist, without asking any of them how they are.
    ///
    /// Costs nothing beyond reading the table, so the sources screen renders
    /// from this and then fills in states from
    /// <see cref="DescribeSourcesAsync"/>. Splitting the two is the difference
    /// between a screen that appears at once and one that waits on the slowest
    /// server in the deployment.
    /// </summary>
    Task<IReadOnlyList<KnowledgeSourceSummaryViewModel>> ListSourcesAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Each source and its current state, for the sources screen. Reports
    /// rather than searches — a screen that had to ask a question to draw
    /// itself would be both slow and misleading.
    ///
    /// One MCP handshake per remote server, so this takes as long as the
    /// slowest of them. Deliberately not cached: the screen exists to say
    /// whether a source is working *now*, and a remembered "active" for a
    /// server that has since died is the one answer it must never give.
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
/// <param name="SourcesWithoutMatches">
/// The display name of every source that was searched successfully and matched
/// nothing. Not a degradation: the source did its job and the honest answer was
/// "no". Reported because from the outside that is indistinguishable from a
/// source that was never searched at all — which is what it was mistaken for.
///
/// Names rather than sentences, unlike <paramref name="Degradations"/>. A
/// source matching nothing is the ordinary case and several will on most
/// questions, so this renders as a short list; a paragraph per source would
/// bury the answer it belongs to.
/// </param>
public sealed record GroundingResult(
    IReadOnlyList<RetrievedPassage> Passages,
    IReadOnlyList<string> Degradations,
    IReadOnlyList<string> SourcesWithoutMatches);
