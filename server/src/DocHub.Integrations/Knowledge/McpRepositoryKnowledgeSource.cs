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
/// <b>The tool contract.</b> MCP does not define what a "search" tool is called
/// or what it returns, so this expects a convention rather than discovering
/// meaning at run time:
/// </para>
/// <list type="bullet">
/// <item>
/// Called with <c>query</c> and <c>maxResults</c>.
/// </item>
/// <item>
/// Returns structured content shaped <c>{ "results": [ { "path", "lines",
/// "text", "url", "score" } ] }</c>, where <c>text</c> is the matching source
/// <b>verbatim</b>.
/// </item>
/// <item>
/// Or, failing that, plain text blocks — one passage each, with the tool name
/// standing in for a path.
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
    RepositorySourceOptions source,
    IRepositorySourceSettings settings,
    IOptions<KnowledgeSourceOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<McpRepositoryKnowledgeSource> logger) : IKnowledgeSource
{
    private readonly KnowledgeSourceOptions options = options.Value;

    public string Name => source.Name;

    public string DisplayName => source.ResolvedDisplayName;

    public string Description => source.ResolvedDescription;

    public async Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default)
    {
        // Null would mean this instance outlived its configuration entry, which
        // cannot happen — the instance is built from that entry at startup.
        var state = await settings.GetAsync(Name, ct)
            ?? throw new InvalidOperationException(
                $"No repository source named '{Name}' is declared.");

        // Off by design is not a fault, and must not render like one — the same
        // three-state reasoning the stub follows.
        if (state.Endpoint is null)
        {
            return new KnowledgeSourceStatus(
                KnowledgeSourceState.Inactive,
                "No MCP server address is set, so answers are grounded in documents only. "
                + "An administrator can set one on this screen.");
        }

        if (!state.IsEnabled)
        {
            return new KnowledgeSourceStatus(
                KnowledgeSourceState.Inactive,
                $"Switched off by an administrator. The address ({state.Endpoint}) is kept, so "
                + "turning it back on needs no retyping.");
        }

        try
        {
            await using var client = await ConnectAsync(state.Endpoint, ct);
            var tool = await ResolveToolAsync(client, ct);

            return new KnowledgeSourceStatus(
                KnowledgeSourceState.Active,
                $"Connected to {state.Endpoint}, searching with the '{tool.Name}' tool.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "MCP server at {Endpoint} could not be reached", state.Endpoint);

            // Configured but not answering is the one case that *is* a fault,
            // and the detail names the address so it can be checked.
            return new KnowledgeSourceStatus(
                KnowledgeSourceState.Unavailable,
                $"{state.Endpoint} did not answer ({exception.Message}).");
        }
    }

    public async Task<KnowledgeSearchResult> SearchAsync(
        KnowledgeQuery query,
        CancellationToken ct = default)
    {
        var state = await settings.GetAsync(Name, ct);

        // Nothing configured is an empty answer, not an exception: the composite
        // reports exceptions as degradations, and "an administrator has not set
        // this up" is not a degradation of anything.
        if (state?.Endpoint is null || !state.IsEnabled) return KnowledgeSearchResult.Empty;

        // A folder filter is a document-shaped idea. A repository has no notion
        // of it, so it is ignored rather than answered with nothing — narrowing
        // to a folder must not silently switch this source off.
        await using var client = await ConnectAsync(state.Endpoint, ct);
        var tool = await ResolveToolAsync(client, ct);

        var take = Math.Clamp(Math.Min(query.Take, options.RepositoryMaxResults), 1, 100);

        var response = await client.CallToolAsync(
            tool.Name,
            new Dictionary<string, object?>
            {
                ["query"] = query.Text,
                ["maxResults"] = take,
            },
            cancellationToken: ct);

        if (response.IsError == true)
        {
            // Thrown, not swallowed: the caller isolates each source, and
            // reporting this as "no results" would hide a broken server behind
            // an answer that merely looks thin.
            throw new InvalidOperationException(
                $"The '{tool.Name}' tool reported an error: {FirstText(response) ?? "no detail given"}.");
        }

        var results = ReadResults(response, tool.Name).Take(take).ToList();

        logger.LogInformation(
            "MCP source returned {Count} passages from {Endpoint} via '{Tool}'",
            results.Count, state.Endpoint, tool.Name);

        return new KnowledgeSearchResult(results);
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

    private async Task<McpClientTool> ResolveToolAsync(McpClient client, CancellationToken ct)
    {
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        if (!string.IsNullOrWhiteSpace(source.ToolName))
        {
            return tools.FirstOrDefault(tool =>
                    tool.Name.Equals(source.ToolName, StringComparison.OrdinalIgnoreCase))
                // Named explicitly and absent means the configuration is wrong,
                // and saying which tools do exist is what makes that fixable.
                ?? throw new InvalidOperationException(
                    $"The server exposes no tool named '{source.ToolName}'. "
                    + $"Available: {(tools.Count == 0 ? "none" : string.Join(", ", tools.Select(t => t.Name)))}.");
        }

        return tools.FirstOrDefault(tool =>
                tool.Name.Contains("search", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "The server exposes no tool with 'search' in its name. Set this source's "
                + "ToolName to choose one explicitly. "
                + $"Available: {(tools.Count == 0 ? "none" : string.Join(", ", tools.Select(t => t.Name)))}.");
    }

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

            if (TryParseJson(block.Text, out var parsed)
                && TryReadHits(parsed, toolName, out var textHits))
            {
                passages.AddRange(textHits);
                continue;
            }

            // Prose, or JSON in a shape we do not recognise. Passed through
            // whole rather than guessed at: the text is still verbatim, so it
            // can still ground an answer — it just cannot be located precisely.
            passages.Add(new KnowledgeResult(
                KnowledgeResultKind.External,
                toolName,
                "result",
                block.Text,
                0,
                "mcp",
                ChunkId: StableChunkId(block.Text)));
        }

        return passages;
    }

    private bool TryReadHits(
        JsonElement payload,
        string toolName,
        out IReadOnlyList<KnowledgeResult> hits)
    {
        hits = [];

        if (payload.ValueKind != JsonValueKind.Object
            || !TryGetProperty(payload, "results", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

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

            var path = Text(element, "path") ?? toolName;
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
        return results.Count > 0;
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
