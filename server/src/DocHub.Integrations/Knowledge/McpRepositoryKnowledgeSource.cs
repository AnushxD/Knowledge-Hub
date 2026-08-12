using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Searches a repository over MCP and offers what it finds as grounding.
///
/// Replaces <see cref="NullRepositoryKnowledgeSource"/> when
/// <c>KnowledgeSources:RepositoryProvider</c> is "mcp". Everything around it —
/// the fan-out, the per-source deadline, failure isolation, rank fusion — was
/// already exercised against the stub, so this arrives into a shape that works
/// rather than being the first second source anyone ever ran.
///
/// <para>
/// <b>Every tool that can be asked is asked</b>, not just the one whose name
/// says "search". A server's tools are different windows on the same
/// repository, and the one that happens to be called <c>search_code</c> is not
/// reliably the one that knows the answer — see
/// <see cref="RepositoryToolPlan"/> for what disqualifies a tool and why. They
/// are asked concurrently over one connection, and merged by rank rather than
/// by score, because two tools on the same server score in no more comparable
/// units than two servers do.
/// </para>
/// <para>
/// <b>The cost, stated plainly.</b> A search tool returns text out of the
/// repository, so a citation to it resolves to something a reader can go and
/// check. A tool like <c>get_architecture</c> returns the server's own prose,
/// and a citation to that resolves only to "this server said this". Both are
/// verbatim — the assistant still quotes exactly what it was handed, and the
/// citation verifier still checks every marker — but they are not equally
/// checkable, which is why search-shaped tools are asked first and win ties.
/// Pinning a source to one tool remains available for a server where that
/// distinction has to be absolute.
/// </para>
/// <para>
/// <b>The tool contract.</b> MCP does not define what a search tool is called
/// or what it returns, so this expects a convention rather than discovering
/// meaning at run time:
/// </para>
/// <list type="bullet">
/// <item>
/// Called with <c>query</c> and <c>maxResults</c>, or whatever the tool's own
/// schema calls them.
/// </item>
/// <item>
/// Returns structured content shaped <c>{ "results": [ { "path", "lines",
/// "text", "url", "score" } ] }</c>, where <c>text</c> is the matching source
/// <b>verbatim</b>.
/// </item>
/// <item>
/// Or, failing that, plain text blocks — one passage each, titled with the
/// server's display name, since there is no path to point at.
/// </item>
/// </list>
/// <para>
/// The verbatim requirement is not a preference. The assistant cites what it
/// was given, so a server that returns a summary would have the assistant
/// quoting text that exists nowhere. That cannot be detected from here, which
/// is why it is stated as a contract the server must meet.
/// </para>
/// </summary>
internal sealed class McpRepositoryKnowledgeSource(
    RepositorySourceDescriptor source,
    IOptions<KnowledgeSourceOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<McpRepositoryKnowledgeSource> logger) : IKnowledgeSource
{
    private readonly KnowledgeSourceOptions options = options.Value;

    public string Name => source.Name;

    public string DisplayName => source.DisplayName;

    /// <summary>
    /// Empty on purpose. A repository server is identified by its display name
    /// and by the status line under it, which names the address and the tool it
    /// is searching with — both true of this server and only this one. A
    /// generic sentence in between would read the same on every one of them.
    /// </summary>
    public string Description => string.Empty;

    public async Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default)
    {
        // Off by design is not a fault, and must not render like one — the same
        // three-state reasoning the stub follows.
        if (!source.IsEnabled)
        {
            return new KnowledgeSourceStatus(
                KnowledgeSourceState.Inactive,
                $"Switched off by an administrator. The address ({source.Endpoint}) is kept, so "
                + "turning it back on needs no retyping.");
        }

        try
        {
            await using var client = await ConnectAsync(source.Endpoint, ct);

            IReadOnlyList<McpClientTool> tools;

            try
            {
                tools = await ResolveToolsAsync(client, ct);
            }
            catch (InvalidOperationException exception)
            {
                // It answered. What is wrong is which tools it has — a named
                // tool that is absent, or nothing that can be asked anything —
                // and reporting that as "did not answer" would send somebody to
                // check a network that is fine.
                return new KnowledgeSourceStatus(
                    KnowledgeSourceState.Unavailable, exception.Message);
            }

            return new KnowledgeSourceStatus(
                KnowledgeSourceState.Active,
                $"Connected to {source.Endpoint}, searching with {Describe(tools)}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "MCP server at {Endpoint} could not be reached", source.Endpoint);

            // Added and not answering is the one case that *is* a fault, and
            // the detail names the address so it can be checked.
            return new KnowledgeSourceStatus(
                KnowledgeSourceState.Unavailable,
                $"{source.Endpoint} did not answer ({exception.Message}).");
        }
    }

    public async Task<KnowledgeSearchResult> SearchAsync(
        KnowledgeQuery query,
        CancellationToken ct = default)
    {
        // Switched off is an empty answer, not an exception: the composite
        // reports exceptions as degradations, and "an administrator turned this
        // off" is not a degradation of anything.
        if (!source.IsEnabled) return KnowledgeSearchResult.Empty;

        // A folder filter is a document-shaped idea. A repository has no notion
        // of it, so it is ignored rather than answered with nothing — narrowing
        // to a folder must not silently switch this source off.
        await using var client = await ConnectAsync(source.Endpoint, ct);
        var tools = await ResolveToolsAsync(client, ct);

        var take = Math.Clamp(Math.Min(query.Take, options.RepositoryMaxResults), 1, 100);

        // Concurrently, over the one connection: the tools are asked the same
        // question and do not depend on each other, and the source is already
        // running under the composite's deadline — so asking them in turn would
        // spend that whole budget on however many tools the server happens to
        // expose.
        var answers = await Task.WhenAll(
            tools.Select(tool => AskAsync(client, tool, query.Text, take, ct)));

        var failed = answers.Where(answer => answer.Failure is not null).ToList();

        // Every tool failing is the server failing, and it is thrown for the
        // same reason a single tool's error always was: the composite isolates
        // each source, and reporting this as "no results" would hide a broken
        // server behind an answer that merely looks thin. One tool of several
        // failing is different — that is a degradation, reported below.
        if (failed.Count > 0 && failed.Count == answers.Length)
        {
            throw new InvalidOperationException(
                failed.Count == 1
                    ? failed[0].Failure!
                    : $"None of the {failed.Count} tools asked could answer. "
                      + string.Join(" ", failed.Select(answer => answer.Failure)));
        }

        var results = Interleave(answers).Take(take).ToList();

        logger.LogInformation(
            "MCP source returned {Count} passages from {Endpoint} via {Tools}",
            results.Count, source.Endpoint, string.Join(", ", tools.Select(tool => tool.Name)));

        return new KnowledgeSearchResult(results, Degradation(failed));
    }

    /// <summary>
    /// Puts the question to one tool, turning a failure into an explanation
    /// rather than an exception.
    ///
    /// Isolated per tool for the same reason the composite isolates each
    /// source: one tool that is broken, or that this server never really meant
    /// to be searched, must not lose an answer the rest of the server could
    /// have grounded.
    /// </summary>
    private async Task<ToolAnswer> AskAsync(
        McpClient client,
        McpClientTool tool,
        string text,
        int take,
        CancellationToken ct)
    {
        // Read off the tool's own schema rather than assumed. A server that
        // calls its query "q" would otherwise be sent an argument it does not
        // know, ignore it, search for the empty string, and hand back something
        // useless that we would then offer the model as grounding.
        var arguments = RepositoryToolArguments.Map(tool.ProtocolTool.InputSchema, text, take);

        if (arguments.QueryParameter is null)
        {
            logger.LogWarning(
                "The '{Tool}' tool on {Endpoint} declares no string parameter, so the query is "
                + "being sent as 'query' and may be ignored",
                tool.Name, source.Endpoint);
        }

        if (arguments.UnfilledRequired.Count > 0)
        {
            // Not fatal — the server may default them — but it is the first
            // thing to look at when this source returns nothing useful. Only
            // reachable for a tool named explicitly on the source; discovery
            // leaves these out rather than calling them wrong.
            logger.LogWarning(
                "The '{Tool}' tool on {Endpoint} requires {Parameters}, which nothing here can "
                + "supply",
                tool.Name, source.Endpoint, string.Join(", ", arguments.UnfilledRequired));
        }

        try
        {
            var response = await client.CallToolAsync(
                tool.Name, arguments.Arguments, cancellationToken: ct);

            if (response.IsError == true)
            {
                return ToolAnswer.Failed(
                    tool.Name,
                    $"The '{tool.Name}' tool reported an error: "
                    + $"{FirstText(response) ?? "no detail given"}.");
            }

            return new ToolAnswer(tool.Name, [.. ReadResults(response, tool.Name).Take(take)], null);
        }
        // The caller giving up, or this source's own deadline expiring, applies
        // to every tool — so it is left to unwind rather than recorded as this
        // one tool having a problem.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "The '{Tool}' tool on {Endpoint} could not be called", tool.Name, source.Endpoint);

            return ToolAnswer.Failed(
                tool.Name, $"The '{tool.Name}' tool could not be called ({exception.Message}).");
        }
    }

    /// <summary>
    /// Merges the tools' answers by rank: each tool's best, then each tool's
    /// second, and so on.
    ///
    /// By rank and never by score, the same rule the composite follows across
    /// sources and for the same reason — a search tool's cosine similarity and
    /// whatever number some other tool reports are not on one scale, and one of
    /// them usually reports nothing at all. <see cref="RepositoryToolPlan"/>
    /// has already put the search-shaped tools first, so they take the ties.
    ///
    /// Deduplicated on the passage id, because two tools on one server looking
    /// at one repository will return the same file, and spending two citation
    /// slots on it would make an answer look better supported than it is.
    /// </summary>
    private static IEnumerable<KnowledgeResult> Interleave(IReadOnlyList<ToolAnswer> answers)
    {
        var seen = new HashSet<int>();
        var deepest = answers.Count == 0 ? 0 : answers.Max(answer => answer.Results.Count);

        for (var rank = 0; rank < deepest; rank++)
        {
            foreach (var answer in answers)
            {
                if (rank >= answer.Results.Count) continue;

                var result = answer.Results[rank];

                if (seen.Add(result.ChunkId)) yield return result;
            }
        }
    }

    /// <summary>
    /// What to tell the reader when some tools answered and others did not.
    ///
    /// Named on the answer rather than logged, exactly as a whole failed source
    /// is: an answer grounded in less than usual has to say so, and "the server
    /// worked but half of it did not" is precisely the case a reader cannot
    /// otherwise see.
    /// </summary>
    private string? Degradation(IReadOnlyList<ToolAnswer> failed) =>
        failed.Count == 0
            ? null
            : $"{source.DisplayName} answered without "
              + $"{string.Join(", ", failed.Select(answer => $"'{answer.Tool}'"))}, so this answer "
              + "draws on the rest of its tools only.";

    /// <param name="Failure">Null when the tool answered, whether or not it found anything.</param>
    private sealed record ToolAnswer(
        string Tool,
        IReadOnlyList<KnowledgeResult> Results,
        string? Failure)
    {
        public static ToolAnswer Failed(string tool, string detail) => new(tool, [], detail);
    }

    private async Task<McpClient> ConnectAsync(string endpoint, CancellationToken ct)
    {
        // A client per operation rather than one cached for the process. The
        // address is an administrator setting that can change between questions,
        // so a cached connection would need invalidating on every edit — and the
        // handshake is cheap next to the search it precedes.
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(endpoint) },
            loggerFactory);

        return await McpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: ct);
    }

    /// <summary>
    /// The tools this question goes to.
    ///
    /// Naming one on the source still pins it to that tool alone. It is now a
    /// narrowing rather than the ordinary path, and it stays supported for
    /// exactly that: a server with one tool worth trusting, or one whose other
    /// tools are expensive enough that asking them on every question is not
    /// wanted. Nothing is inferred about a tool somebody named — an
    /// administrator saying "use this one" outranks every rule here.
    /// </summary>
    private async Task<IReadOnlyList<McpClientTool>> ResolveToolsAsync(
        McpClient client,
        CancellationToken ct)
    {
        var tools = (await client.ListToolsAsync(cancellationToken: ct)).ToList();

        if (!string.IsNullOrWhiteSpace(source.ToolName))
        {
            return
            [
                tools.FirstOrDefault(tool =>
                        tool.Name.Equals(source.ToolName, StringComparison.OrdinalIgnoreCase))
                    // Named explicitly and absent means the configuration is
                    // wrong, and saying which tools do exist makes that fixable.
                    ?? throw new InvalidOperationException(
                        $"The server exposes no tool named '{source.ToolName}'. "
                        + $"Available: {Available(tools)}."),
            ];
        }

        var answerable = RepositoryToolPlan.Answerable(tools);

        // Reachable, speaking MCP, and still unable to ground anything: every
        // tool either changes something or has nowhere to put the question.
        // That is a different problem from an outage and has to read like one.
        if (answerable.Count == 0)
        {
            throw new InvalidOperationException(
                "The server exposes no read-only tool that a question can be put to. Set this "
                + $"source's ToolName to choose one explicitly. Available: {Available(tools)}.");
        }

        return answerable;
    }

    private static string Available(IReadOnlyList<McpClientTool> tools) =>
        tools.Count == 0 ? "none" : string.Join(", ", tools.Select(tool => tool.Name));

    /// <summary>
    /// The tools named on a status line. Listed rather than counted: "searching
    /// with 4 of its tools" invites the question this sentence exists to
    /// answer, and a server with forty tools is not the case being designed for.
    /// </summary>
    private static string Describe(IReadOnlyList<McpClientTool> tools) =>
        tools.Count == 1
            ? $"the '{tools[0].Name}' tool"
            : $"the {string.Join(", ", tools.Select(tool => $"'{tool.Name}'"))} tools";

    /// <summary>
    /// Turns a tool response into passages.
    ///
    /// Three shapes are accepted, in order, because MCP servers genuinely differ
    /// here and the difference is not worth pushing onto whoever runs one:
    ///
    /// <list type="number">
    /// <item>structured content carrying a <c>results</c> array;</item>
    /// <item>a text block whose body <i>is</i> that JSON — which is what a tool
    /// returning an object produces when it declares no output schema, and is
    /// the common case rather than the exotic one;</item>
    /// <item>plain prose, taken as one passage per block.</item>
    /// </list>
    /// </summary>
    private IReadOnlyList<KnowledgeResult> ReadResults(CallToolResult response, string toolName)
    {
        if (response.StructuredContent is { } structured
            && TryReadHits(structured, toolName, out var structuredHits))
        {
            return structuredHits;
        }

        var passages = new List<KnowledgeResult>();

        foreach (var block in response.Content.OfType<TextContentBlock>())
        {
            if (string.IsNullOrWhiteSpace(block.Text)) continue;

            if (TryReadEnvelope(block.Text, toolName, out var textHits))
            {
                // Honoured even when empty: the server said it found nothing,
                // and that is an answer, not an absence of one.
                passages.AddRange(textHits);
                continue;
            }

            // Prose, or JSON in a shape we do not recognise. Passed through
            // whole rather than guessed at: the text is still verbatim, so it
            // can still ground an answer — it just cannot be located precisely.
            //
            // Titled with the server, not the tool that answered it. A citation
            // is read by someone deciding whether to trust a sentence, and
            // "search" tells them nothing while "Live scores" tells them where
            // it came from.
            //
            // The tool goes in the heading instead, which is the "where within
            // it" slot and is now load-bearing: several tools on one server
            // answer in prose, and two citations reading "Repositories ·
            // result" would be indistinguishable.
            passages.Add(new KnowledgeResult(
                KnowledgeResultKind.External,
                source.DisplayName,
                toolName,
                block.Text,
                0,
                "mcp",
                ChunkId: StableChunkId(block.Text)));
        }

        return passages;
    }


    /// <summary>
    /// Reads a results envelope out of a text block, whether the block is JSON
    /// or JSON wrapped in a sentence.
    ///
    /// Servers announce themselves: <c>Search results for 'x':</c> followed by
    /// the object. That is not parseable JSON, so treating the block as prose
    /// was the only option — which meant a plain "no matches" arrived as a
    /// passage of text for the model to cite. Taking the object out of the
    /// sentence costs one substring and removes the whole class of problem.
    ///
    /// Nothing is guessed: the extracted span still has to parse and still has
    /// to carry a recognised hit array, so a prose passage that merely contains
    /// braces — a code snippet, say — falls through to being prose, as it
    /// should.
    /// </summary>
    private bool TryReadEnvelope(
        string text,
        string toolName,
        out IReadOnlyList<KnowledgeResult> hits)
    {
        if (TryParseJson(text, out var whole) && TryReadHits(whole, toolName, out hits))
            return true;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start >= 0 && end > start
            && TryParseJson(text[start..(end + 1)], out var embedded)
            && TryReadHits(embedded, toolName, out hits))
        {
            return true;
        }

        hits = [];
        return false;
    }

    private bool TryReadHits(
        JsonElement payload,
        string toolName,
        out IReadOnlyList<KnowledgeResult> hits)
    {
        hits = [];

        if (payload.ValueKind != JsonValueKind.Object || !TryGetHitArray(payload, out var array))
            return false;

        var results = new List<KnowledgeResult>();

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var text = Text(element, "text");

            // A hit with no text cannot ground anything, and citing it would
            // point a reader at a passage the model never saw.
            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogDebug("Skipping an MCP hit from '{Tool}' that carried no text", toolName);
                continue;
            }

            // The path is the honest title when the server supplies one — it
            // names the actual file. Falling back to the server rather than the
            // tool, for the same reason as above.
            var path = Text(element, "path") ?? source.DisplayName;
            var lines = Text(element, "lines") ?? "match";
            var url = Text(element, "url");

            results.Add(new KnowledgeResult(
                KnowledgeResultKind.External,
                path,
                lines,
                text,
                TryGetProperty(element, "score", out var score) && score.TryGetDouble(out var parsed)
                    ? parsed
                    : 0,
                "mcp",
                Url: url,
                ChunkId: StableChunkId(url ?? $"{path}#{lines}")));
        }

        hits = results;

        // Three outcomes, and the middle one is the point.
        //
        // An *empty* array is an answer: the server understood and found
        // nothing. Reporting that as unrecognised would send it to the prose
        // fallback, which hands the model the raw JSON as though it were a
        // passage worth citing — that is how a live-scores server became a
        // source for a question about orange juice.
        //
        // A non-empty array whose entries carry no text is different: there is
        // real content here in a shape this does not know how to take apart.
        // Claiming it as zero hits would silently drop results the server did
        // find, so it goes to the prose fallback intact.
        if (array.GetArrayLength() > 0 && results.Count == 0) return false;

        return true;
    }

    private static bool TryParseJson(string text, out JsonElement element)
    {
        element = default;

        // Cheap reject before paying for a parse: most tool output is prose.
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return false;

        try
        {
            // Cloned because JsonDocument owns pooled buffers it returns on
            // dispose, and the element outlives this method.
            using var document = JsonDocument.Parse(text);
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Property lookup that tolerates casing. The shape is a convention we ask
    /// servers to follow, and failing over "Results" against "results" would be
    /// a needlessly sharp edge.
    /// </summary>

    /// <summary>
    /// The array of hits in a results envelope, whatever the server called it.
    ///
    /// MCP standardises none of this. The documented shape is <c>results</c>,
    /// but <c>result</c> is just as common — one server in use here answers
    /// with it — and failing to recognise an envelope is not harmless: it sends
    /// a perfectly clear "nothing matched" down the prose path to be treated as
    /// content.
    /// </summary>
    private static bool TryGetHitArray(JsonElement payload, out JsonElement array)
    {
        foreach (var name in HitArrayNames)
        {
            if (TryGetProperty(payload, name, out array) && array.ValueKind == JsonValueKind.Array)
                return true;
        }

        array = default;
        return false;
    }

    private static readonly string[] HitArrayNames = ["results", "result", "matches", "hits", "items"];

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// The detail an errored tool call carried, if it carried any. MCP puts the
    /// explanation in the content blocks rather than in a status field.
    /// </summary>
    private static string? FirstText(CallToolResult response) =>
        response.Content.OfType<TextContentBlock>()
            .Select(block => block.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

    private static string? Text(JsonElement element, string property) =>
        TryGetProperty(element, property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// A stable, non-negative id for a passage that has no chunk ordinal.
    ///
    /// Deduplication and citation identity key on this, so two different files
    /// must not collapse to one — which is exactly what returning zero for
    /// everything would do. Derived from the locator so the same passage keeps
    /// the same id across questions.
    /// </summary>
    private static int StableChunkId(string locator) =>
        Math.Abs(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(locator)) is var hash
            ? BitConverter.ToInt32(hash, 0)
            : 0);
}
