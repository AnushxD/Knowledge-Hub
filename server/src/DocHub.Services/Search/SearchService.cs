using System.Diagnostics;
using System.Text;
using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Embeddings;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Search;

internal sealed class SearchService(
    IChunkRepository chunks,
    IEmbeddingProvider embeddings,
    ILogger<SearchService> logger) : ISearchService
{
    /// <summary>
    /// Reciprocal rank fusion constant. 60 is the value from the original RRF
    /// paper and behaves well without tuning: large enough that the top few
    /// ranks are not winner-take-all, small enough that rank still dominates.
    /// </summary>
    private const double RankFusionConstant = 60;

    /// <summary>Characters of context shown around the matched text.</summary>
    private const int SnippetLength = 320;

    public async Task<SearchResponseViewModel> SearchAsync(
        SearchRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var (query, take, keyword, vector, vectorError) = await RankAsync(request, ct);

        var fused = Fuse(keyword, vector);
        var terms = ExtractTerms(query);

        var results = fused
            .Take(take)
            .Select(match => ToViewModel(match, terms))
            .ToList();

        stopwatch.Stop();

        logger.LogInformation(
            "Search '{Query}' matched {Keyword} keyword and {Vector} vector chunks, "
            + "{Fused} after fusion, in {ElapsedMs}ms",
            query, keyword.Count, vector.Count, fused.Count, stopwatch.ElapsedMilliseconds);

        return new SearchResponseViewModel(
            query,
            fused.Count,
            stopwatch.ElapsedMilliseconds,
            terms,
            results,
            new SearchDiagnosticsViewModel(
                keyword.Count,
                vector.Count,
                embeddings.Name,
                vectorError is null,
                vectorError));
    }

    public async Task<RetrievalResult> RetrieveAsync(
        SearchRequest request,
        CancellationToken ct = default)
    {
        var (_, take, keyword, vector, vectorError) = await RankAsync(request, ct);

        var passages = Fuse(keyword, vector)
            .Take(take)
            .Select(match => new RetrievedPassage(
                match.Chunk.DocumentId,
                match.Chunk.DocumentTitle,
                match.Chunk.Ordinal,
                match.Chunk.SectionRef ?? $"Section {match.Chunk.Ordinal + 1}",
                // Full text, not a snippet — this is what the model reasons over.
                match.Chunk.Text,
                Math.Round(match.Score, 6),
                Describe(match)))
            .ToList();

        return new RetrievalResult(passages, vectorError);
    }

    /// <summary>
    /// Runs both retrieval branches for a request. Shared by search and by
    /// grounding so there is exactly one ranking implementation — if they
    /// diverged, the assistant would cite passages the search screen never
    /// showed, and neither result would explain the other.
    /// </summary>
    private async Task<RankedBranches> RankAsync(SearchRequest request, CancellationToken ct)
    {
        var query = request.Query?.Trim() ?? string.Empty;

        if (query.Length == 0)
            throw new ValidationException("Enter something to search for.");

        var take = Math.Clamp(request.Take, 1, 100);

        var filter = new ChunkSearchDto
        {
            Text = query,
            FolderId = request.FolderId,
            Extensions = request.Extension?
                .Select(extension => extension.TrimStart('.').ToLowerInvariant())
                .ToList(),
            Tags = request.Tag?.Select(tag => tag.Trim().ToLowerInvariant()).ToList(),
            OwnerId = request.OwnerId,
            // Deeper than the page size: fusion can only reorder what it was
            // given, so each branch has to offer more than the caller will see.
            Limit = Math.Max(take * 3, 40),
        };

        // Start embedding first, then run the keyword query while it is in
        // flight. The embedding call is a network round trip and by far the
        // slowest part of a search, so this overlaps the only latency worth
        // overlapping.
        //
        // The two database queries themselves are deliberately *not*
        // concurrent: they share a request-scoped DbContext, which cannot serve
        // two commands at once. Issuing them together fails outright — and
        // fails intermittently, because a slow embedding provider hides the
        // race by letting the keyword query finish first.
        var embeddingTask = EmbedQueryAsync(query, ct);

        var keyword = await chunks.SearchKeywordAsync(filter, ct);

        var (embedding, embeddingError) = await embeddingTask;
        var (vector, vectorError) = embedding is null
            ? ([], embeddingError)
            : await SearchVectorAsync(filter, embedding, ct);

        return new RankedBranches(query, take, keyword, vector, vectorError);
    }

    private sealed record RankedBranches(
        string Query,
        int Take,
        IReadOnlyList<ChunkMatchDto> Keyword,
        IReadOnlyList<ChunkMatchDto> Vector,
        string? VectorError);

    /// <summary>
    /// Embeds the query, degrading to keyword-only if the provider is down.
    ///
    /// A search that returns the keyword half is far more useful than an error
    /// page, so this failure is reported in the diagnostics rather than thrown
    /// — but it is never hidden.
    /// </summary>
    private async Task<(float[]? Embedding, string? Error)> EmbedQueryAsync(
        string query,
        CancellationToken ct)
    {
        try
        {
            return (await embeddings.EmbedQueryAsync(query, ct), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Embedding the query failed; falling back to keyword-only results.");

            return (null, Unavailable(exception));
        }
    }

    private async Task<(IReadOnlyList<ChunkMatchDto> Matches, string? Error)> SearchVectorAsync(
        ChunkSearchDto filter,
        float[] embedding,
        CancellationToken ct)
    {
        try
        {
            return (await chunks.SearchVectorAsync(filter, embedding, ct), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Vector search failed; falling back to keyword-only results.");

            return ([], Unavailable(exception));
        }
    }

    private static string Unavailable(Exception exception) =>
        $"Semantic matching is unavailable ({exception.Message}). Showing keyword results only.";

    /// <summary>
    /// Merges the two branches by reciprocal rank fusion: each chunk scores
    /// 1/(k + rank) in every branch that found it, and the scores add.
    ///
    /// Fusing on rank rather than on the branches' own scores is the whole
    /// point — ts_rank and cosine similarity are on unrelated scales, and any
    /// attempt to weight them directly would be arbitrary. A chunk both
    /// branches agree on naturally outranks one only a single branch found.
    /// </summary>
    private static List<FusedMatch> Fuse(
        IReadOnlyList<ChunkMatchDto> keyword,
        IReadOnlyList<ChunkMatchDto> vector)
    {
        var scores = new Dictionary<Guid, FusedMatch>();

        void Accumulate(IReadOnlyList<ChunkMatchDto> branch, bool isKeyword)
        {
            for (var rank = 0; rank < branch.Count; rank++)
            {
                var match = branch[rank];
                var contribution = 1 / (RankFusionConstant + rank + 1);

                if (scores.TryGetValue(match.ChunkId, out var existing))
                {
                    scores[match.ChunkId] = existing with
                    {
                        Score = existing.Score + contribution,
                        FoundByKeyword = existing.FoundByKeyword || isKeyword,
                        FoundByVector = existing.FoundByVector || !isKeyword,
                    };
                }
                else
                {
                    scores[match.ChunkId] = new FusedMatch(
                        match, contribution, isKeyword, !isKeyword);
                }
            }
        }

        Accumulate(keyword, isKeyword: true);
        Accumulate(vector, isKeyword: false);

        return [.. scores.Values.OrderByDescending(match => match.Score)];
    }

    private static SearchResultViewModel ToViewModel(FusedMatch match, IReadOnlyList<string> terms)
    {
        var chunk = match.Chunk;

        return new SearchResultViewModel(
            chunk.DocumentId,
            chunk.DocumentTitle,
            chunk.FileName,
            chunk.Extension,
            chunk.FolderId,
            chunk.FolderPath,
            chunk.Ordinal,
            chunk.SectionRef ?? $"Section {chunk.Ordinal + 1}",
            Snippet(chunk.Text, terms),
            Math.Round(match.Score, 6),
            Describe(match));
    }

    /// <summary>Which branch or branches found a match.</summary>
    private static string Describe(FusedMatch match) =>
        (match.FoundByKeyword, match.FoundByVector) switch
        {
            (true, true) => "both",
            (true, false) => "keyword",
            _ => "vector",
        };

    /// <summary>
    /// A readable window of the chunk, centred on the first query term it
    /// contains. Pure vector matches often contain no query term at all, and
    /// fall back to the opening of the chunk.
    /// </summary>
    private static string Snippet(string text, IReadOnlyList<string> terms)
    {
        var collapsed = CollapseWhitespace(text);

        if (collapsed.Length <= SnippetLength) return collapsed;

        var position = -1;
        foreach (var term in terms)
        {
            position = collapsed.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (position >= 0) break;
        }

        if (position < 0) return collapsed[..SnippetLength].TrimEnd() + "…";

        var start = Math.Max(0, position - SnippetLength / 3);
        var length = Math.Min(SnippetLength, collapsed.Length - start);

        // Nudge to word boundaries so the snippet never opens or closes
        // mid-word.
        if (start > 0)
        {
            var space = collapsed.IndexOf(' ', start);
            if (space > 0 && space - start < 30) start = space + 1;
        }

        length = Math.Min(length, collapsed.Length - start);
        var window = collapsed.Substring(start, length).Trim();

        return (start > 0 ? "…" : string.Empty)
            + window
            + (start + length < collapsed.Length ? "…" : string.Empty);
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;

        foreach (var character in text)
        {
            var isSpace = char.IsWhiteSpace(character);
            if (isSpace && previousWasSpace) continue;

            builder.Append(isSpace ? ' ' : character);
            previousWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Common English words Postgres' "english" configuration already strips
    /// from the query. Highlighting them would mark half of every snippet, so
    /// the client is not given them in the first place.
    ///
    /// Deliberately short rather than a full stop-word list: these are the ones
    /// that actually show up in questions ("how do I log in to the VPN"), and a
    /// term wrongly kept costs one extra highlight, not a wrong result.
    /// </summary>
    private static readonly HashSet<string> StopWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "are", "as", "at", "be", "but", "by", "can", "do", "does",
            "for", "from", "get", "had", "has", "have", "how", "i", "if", "in", "into", "is",
            "it", "its", "me", "my", "no", "not", "of", "on", "or", "our", "out", "should",
            "so", "that", "the", "their", "then", "there", "these", "they", "this", "to", "up",
            "was", "we", "were", "what", "when", "where", "which", "while", "who", "why",
            "will", "with", "would", "you", "your",
        };

    /// <summary>
    /// The words worth highlighting: what the user typed, minus the words that
    /// carry no signal.
    /// </summary>
    private static IReadOnlyList<string> ExtractTerms(string query)
    {
        return
        [
            .. query
                .Split([' ', '\t', '\n', '"', '\'', ',', '.', '?', '!', '(', ')', ':', ';'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => term.Length > 1 && !StopWords.Contains(term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private sealed record FusedMatch(
        ChunkMatchDto Chunk,
        double Score,
        bool FoundByKeyword,
        bool FoundByVector);
}
