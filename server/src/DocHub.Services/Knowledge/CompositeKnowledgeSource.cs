using System.Diagnostics;
using DocHub.Integrations.Knowledge;
using DocHub.Services.Search;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Knowledge;

/// <summary>
/// Searches every configured source at once and merges what comes back.
///
/// Three decisions make this more than a loop:
///
/// <list type="bullet">
/// <item>
/// <b>A failing source degrades the answer instead of losing it.</b> One
/// unreachable MCP server must not turn a question the documents could have
/// answered into an error. If every source fails there is nothing to ground on,
/// and the orchestrator refuses — which is the correct outcome, reached the
/// correct way.
/// </item>
/// <item>
/// <b>Merging is by rank, never by score.</b> Each source scores in its own
/// units — reciprocal rank fusion here, cosine similarity there, whatever a
/// future MCP server reports — and comparing those numbers directly would be
/// arbitrary. Only rank position is honestly comparable, which is the same
/// reason the two search branches are fused this way.
/// </item>
/// <item>
/// <b>Sources run concurrently.</b> Unlike the keyword and vector branches,
/// which share a request-scoped DbContext and must not overlap, sources are
/// separate subsystems — see the invariant on <see cref="SearchAllAsync"/>.
/// </item>
/// </list>
/// </summary>
internal sealed class CompositeKnowledgeSource(
    IEnumerable<IKnowledgeSource> sources,
    ILogger<CompositeKnowledgeSource> logger) : IKnowledgeRetriever
{
    /// <summary>
    /// Reciprocal rank fusion constant, matching <see cref="SearchService"/>.
    /// Same constant for the same reason: rank should dominate without the top
    /// result being winner-take-all.
    /// </summary>
    private const double RankFusionConstant = 60;

    public async Task<GroundingResult> RetrieveAsync(
        SearchRequest request,
        CancellationToken ct = default)
    {
        var text = request.Query?.Trim() ?? string.Empty;

        if (text.Length == 0)
            throw new ValidationException("Enter something to search for.");

        var query = new KnowledgeQuery(
            text,
            request.FolderId,
            // Every source is offered the full budget rather than a share of
            // it. Splitting it would cap how much a strong source can
            // contribute based only on how many other sources happen to be
            // registered — including ones that returned nothing.
            Math.Clamp(request.Take, 1, 100));

        var outcomes = await SearchAllAsync(query, ct);

        var passages = Fuse(outcomes)
            .Take(query.Take)
            .Select(fused => fused.Passage with { Score = Math.Round(fused.Score, 6) })
            .ToList();

        var degradations = outcomes
            .Where(outcome => outcome.Degradation is not null)
            .Select(outcome => outcome.Degradation!)
            .ToList();

        return new GroundingResult(passages, degradations);
    }

    public async Task<IReadOnlyList<KnowledgeSourceViewModel>> DescribeSourcesAsync(
        CancellationToken ct = default)
    {
        var described = await Task.WhenAll(sources.Select(async source =>
        {
            KnowledgeSourceStatus status;

            try
            {
                status = await source.CheckStatusAsync(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "Knowledge source {Source} could not report its status", source.Name);

                // A source that cannot say how it is doing is not healthy, and
                // saying so is the whole purpose of the screen.
                status = new KnowledgeSourceStatus(
                    KnowledgeSourceState.Unavailable,
                    $"This source did not respond ({exception.Message}).");
            }

            return new KnowledgeSourceViewModel(
                source.Name,
                source.DisplayName,
                source.Description,
                status.State.ToString().ToLowerInvariant(),
                status.Detail);
        }));

        // Working sources first, then anything needing attention, rather than
        // whatever order dependency injection happened to hand over. The screen
        // is read top-down to answer "what is grounding my answers right now".
        return
        [
            .. described
                .OrderBy(source => source.State switch
                {
                    "active" => 0,
                    "inactive" => 1,
                    _ => 2,
                })
                .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// Puts the query to every source at once, turning a failure into an empty
    /// result plus an explanation.
    ///
    /// The concurrency is safe on an invariant worth stating plainly: at most
    /// one source touches the request-scoped DbContext — the document source.
    /// Everything else is an out-of-process call. A second database-backed
    /// source added here would have to run sequentially with the first, exactly
    /// as the keyword and vector branches do, or it will fail intermittently
    /// under a fast provider and pass under a slow one.
    /// </summary>
    private async Task<IReadOnlyList<SourceOutcome>> SearchAllAsync(
        KnowledgeQuery query,
        CancellationToken ct)
    {
        return await Task.WhenAll(sources.Select(async source =>
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await source.SearchAsync(query, ct);

                logger.LogInformation(
                    "Knowledge source {Source} returned {Count} passages in {ElapsedMs}ms",
                    source.Name, result.Results.Count, stopwatch.ElapsedMilliseconds);

                return new SourceOutcome(source.Name, result.Results, result.Degradation);
            }
            // A bad request is the caller's fault and applies to every source,
            // so it must surface as a validation error rather than be reported
            // as this source being unwell.
            catch (ValidationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception,
                    "Knowledge source {Source} failed; answering without it", source.Name);

                return new SourceOutcome(
                    source.Name,
                    [],
                    $"{source.DisplayName} could not be searched ({exception.Message}), so "
                    + "nothing from it was used.");
            }
        }));
    }

    /// <summary>
    /// Interleaves the sources by reciprocal rank fusion: a passage scores
    /// 1/(k + rank) in every source that returned it, and the scores add, so a
    /// passage two sources agree on outranks one only a single source found.
    ///
    /// Deduplicates on document and chunk, because two sources indexing the
    /// same file would otherwise spend two citation slots on one passage and
    /// make an answer look better supported than it is.
    /// </summary>
    private static List<FusedPassage> Fuse(IReadOnlyList<SourceOutcome> outcomes)
    {
        var scores = new Dictionary<(Guid DocumentId, int ChunkId), FusedPassage>();

        foreach (var outcome in outcomes)
        {
            for (var rank = 0; rank < outcome.Results.Count; rank++)
            {
                var result = outcome.Results[rank];
                var key = (result.DocumentId, result.ChunkId);
                var contribution = 1 / (RankFusionConstant + rank + 1);

                if (scores.TryGetValue(key, out var existing))
                {
                    scores[key] = existing with { Score = existing.Score + contribution };
                }
                else
                {
                    scores[key] = new FusedPassage(ToPassage(result), contribution);
                }
            }
        }

        return [.. scores.Values.OrderByDescending(passage => passage.Score)];
    }

    private static RetrievedPassage ToPassage(KnowledgeResult result) =>
        new(
            result.DocumentId,
            result.DocumentTitle,
            result.ChunkId,
            result.Heading,
            result.Text,
            result.Score,
            result.MatchedBy);

    /// <param name="Degradation">Null when the source answered fully.</param>
    private sealed record SourceOutcome(
        string Name,
        IReadOnlyList<KnowledgeResult> Results,
        string? Degradation);

    private sealed record FusedPassage(RetrievedPassage Passage, double Score);
}
