using System.Diagnostics;
using DocHub.Integrations.Knowledge;
using DocHub.Services.Search;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// <b>Sources run concurrently, each under its own deadline.</b> Unlike the
/// keyword and vector branches, which share a request-scoped DbContext and must
/// not overlap, sources are separate subsystems — see the invariant on
/// <see cref="SearchAllAsync"/>. Concurrency alone is not enough: the fan-out
/// waits for every source, so one that never replies would hold up an answer
/// the others were ready to give. The deadline is what makes "a failing source
/// degrades the answer" true of a hung source and not just a throwing one.
/// </item>
/// </list>
/// </summary>
internal sealed class CompositeKnowledgeSource(
    IKnowledgeSourceCatalog catalog,
    IOptions<KnowledgeOptions> options,
    ILogger<CompositeKnowledgeSource> logger) : IKnowledgeRetriever
{
    private readonly KnowledgeOptions options = options.Value;

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
        var sources = await catalog.ResolveAsync(ct);

        var described = await Task.WhenAll(sources.Select(async source =>
        {
            KnowledgeSourceStatus status;

            // The same deadline applies here: without it, one unreachable
            // server would hang the screen whose entire job is to tell you that
            // a source is unreachable.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.SourceTimeoutSeconds));

            try
            {
                status = await source.CheckStatusAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                status = new KnowledgeSourceStatus(
                    KnowledgeSourceState.Unavailable,
                    $"This source did not respond within {options.SourceTimeoutSeconds} seconds.");
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
    ///
    /// Resolving the sources is itself a database read, so it finishes before
    /// the fan-out starts rather than racing it on the same DbContext.
    /// </summary>
    private async Task<IReadOnlyList<SourceOutcome>> SearchAllAsync(
        KnowledgeQuery query,
        CancellationToken ct)
    {
        var sources = await catalog.ResolveAsync(ct);

        return await Task.WhenAll(sources.Select(async source =>
        {
            var stopwatch = Stopwatch.StartNew();

            // One deadline per source, linked to the caller's token so a client
            // that goes away still cancels everything immediately.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.SourceTimeoutSeconds));

            try
            {
                var result = await source.SearchAsync(query, deadline.Token);

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
            // Our deadline, not the caller's cancellation — the guard is what
            // tells them apart. A caller who gave up wants the whole request
            // abandoned; a source that ran out of time is just left out.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Knowledge source {Source} exceeded {Timeout}s; answering without it",
                    source.Name, options.SourceTimeoutSeconds);

                return new SourceOutcome(
                    source.Name,
                    [],
                    $"{source.DisplayName} did not respond within "
                    + $"{options.SourceTimeoutSeconds} seconds, so nothing from it was used.");
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
        var scores = new Dictionary<(string Identity, int ChunkId), FusedPassage>();

        foreach (var outcome in outcomes)
        {
            for (var rank = 0; rank < outcome.Results.Count; rank++)
            {
                var result = outcome.Results[rank];
                var key = KeyFor(outcome, result);
                var contribution = 1 / (RankFusionConstant + rank + 1);

                if (scores.TryGetValue(key, out var existing))
                {
                    scores[key] = existing with { Score = existing.Score + contribution };
                }
                else
                {
                    scores[key] = new FusedPassage(ToPassage(outcome, result), contribution);
                }
            }
        }

        return [.. scores.Values.OrderByDescending(passage => passage.Score)];
    }

    /// <summary>
    /// What makes two results "the same passage".
    ///
    /// A document is identified by its id, deliberately without the source name:
    /// two sources returning the same document *should* collapse to one
    /// citation, which is the whole reason this deduplicates. An external
    /// passage has no id we control, so a URL identifies it across sources and,
    /// failing that, the source's own name is folded in — otherwise two sources
    /// that each happen to return "README.md" would be merged into one citation
    /// pointing at whichever arrived first.
    /// </summary>
    private static (string Identity, int ChunkId) KeyFor(
        SourceOutcome outcome,
        KnowledgeResult result) =>
        result is { Kind: KnowledgeResultKind.Document, DocumentId: { } documentId }
            ? (documentId.ToString(), result.ChunkId)
            : (result.Url ?? $"{outcome.Name}:{result.Title}", result.ChunkId);

    private static RetrievedPassage ToPassage(SourceOutcome outcome, KnowledgeResult result) =>
        new(
            result.Kind == KnowledgeResultKind.Document ? PassageKind.Document : PassageKind.External,
            result.Title,
            result.ChunkId,
            result.Heading,
            result.Text,
            result.Score,
            result.MatchedBy,
            DocumentId: result.DocumentId,
            Url: result.Url,
            // Attached here rather than by each source: the name is the
            // composite's own identifier for that source, so a source cannot
            // misattribute its passages to another.
            SourceName: outcome.Name);

    /// <param name="Degradation">Null when the source answered fully.</param>
    private sealed record SourceOutcome(
        string Name,
        IReadOnlyList<KnowledgeResult> Results,
        string? Degradation);

    private sealed record FusedPassage(RetrievedPassage Passage, double Score);
}
