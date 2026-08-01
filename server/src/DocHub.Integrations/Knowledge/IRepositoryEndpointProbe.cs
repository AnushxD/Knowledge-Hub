using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Reports what is actually at an address, before an administrator commits to
/// it.
///
/// It speaks MCP rather than merely knocking on the door, because the mistakes
/// that matter here are not "the host is down" — they are "that is the wrong
/// one of our two servers" and "the tool is not called what you assumed". Both
/// are invisible to an HTTP probe and obvious from a tool list.
///
/// A plain HTTP request is still the fallback: when the handshake fails, "a web
/// server answered but it does not speak MCP" and "nothing is listening" call
/// for completely different things to be done, and only the weaker probe can
/// tell them apart.
/// </summary>
public interface IRepositoryEndpointProbe
{
    Task<EndpointProbeResult> ProbeAsync(string endpoint, CancellationToken ct = default);
}

/// <param name="IsReachable">Something answered, whether or not it spoke MCP.</param>
/// <param name="SpeaksMcp">
/// The handshake succeeded and the tool list was read. Only then are the
/// remaining members meaningful.
/// </param>
/// <param name="Detail">Plain wording for the administrator, naming what failed when it did.</param>
/// <param name="Tools">Every tool the server exposes, in the order it listed them.</param>
/// <param name="SuggestedToolName">
/// The tool searching would pick if this source does not name one — so the
/// screen can offer it rather than making somebody copy it out of a list.
/// </param>
/// <param name="Repositories">
/// What the server says it indexes, when it offers a tool that says so. Empty
/// is normal and means only that it does not — nothing depends on this.
/// </param>
public sealed record EndpointProbeResult(
    bool IsReachable,
    bool SpeaksMcp,
    string Detail,
    IReadOnlyList<string> Tools,
    string? SuggestedToolName,
    IReadOnlyList<string> Repositories)
{
    public static EndpointProbeResult Failed(string detail) =>
        new(false, false, detail, [], null, []);

    /// <summary>Something answered, but not in MCP. Reachable, and not usable.</summary>
    public static EndpointProbeResult NotMcp(string detail) =>
        new(true, false, detail, [], null, []);
}

internal sealed class McpRepositoryEndpointProbe(
    HttpClient http,
    ILoggerFactory loggerFactory,
    ILogger<McpRepositoryEndpointProbe> logger) : IRepositoryEndpointProbe
{
    /// <summary>
    /// A handshake, a tool list and possibly one tool call, with a person
    /// watching a button. Longer than the plain HTTP probe's timeout and still
    /// short enough that "no answer" arrives before impatience does.
    /// </summary>
    private static readonly TimeSpan McpDeadline = TimeSpan.FromSeconds(12);

    /// <summary>Enough to recognise the server; the screen shows a few and counts the rest.</summary>
    private const int MaxRepositoriesReported = 50;

    public async Task<EndpointProbeResult> ProbeAsync(
        string endpoint,
        CancellationToken ct = default)
    {
        // Scheme checked here as well as in the Service that calls this. A bare
        // "host:port" parses as an absolute URI whose *scheme* is the hostname,
        // so accepting anything absolute would have this trying to open
        // "mcp.internal:8080" as a protocol — and the transport's complaint
        // about that helps nobody.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return EndpointProbeResult.Failed(
                "That is not an http:// or https:// address.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(McpDeadline);

        try
        {
            return await ProbeMcpAsync(uri, deadline.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception,
                "MCP handshake with {Endpoint} failed; falling back to an HTTP probe", uri);

            // Not an error yet: the address may be a plain web server, and
            // saying which it is is the entire value of this button.
            return await ProbeHttpAsync(uri, exception, ct);
        }
    }

    private async Task<EndpointProbeResult> ProbeMcpAsync(Uri uri, CancellationToken ct)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = uri }, loggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: loggerFactory, cancellationToken: ct);

        var tools = await client.ListToolsAsync(cancellationToken: ct);
        var names = tools.Select(tool => tool.Name).ToList();
        var search = RepositoryToolNames.PickSearchTool(names);
        var repositories = await ReadRepositoriesAsync(client, names, ct);

        return new EndpointProbeResult(
            IsReachable: true,
            SpeaksMcp: true,
            Describe(names, search, repositories),
            names,
            search,
            repositories);
    }

    private static string Describe(
        IReadOnlyList<string> tools,
        string? search,
        IReadOnlyList<string> repositories)
    {
        var sentence = search is null
            // Reachable, speaks MCP, and still unusable for grounding — a
            // different problem from an outage, so it has to read like one.
            ? $"Connected, but none of its {tools.Count} tools has \"search\" in its name. "
              + "Name the one to search with, or this source will fail on every question."
            : $"Connected. Searching would use \"{search}\".";

        return repositories.Count == 0
            ? sentence
            : $"{sentence} Indexes {repositories.Count} "
              + $"{(repositories.Count == 1 ? "repository" : "repositories")}.";
    }

    /// <summary>
    /// Asks what the server indexes, when it offers a tool that says so.
    ///
    /// Best-effort throughout: this is a description, not grounding. A server
    /// without such a tool, one that errors, or one whose answer is in a shape
    /// nothing here recognises all produce an empty list and no complaint —
    /// failing a probe over the decorative half of it would be absurd.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadRepositoriesAsync(
        McpClient client,
        IReadOnlyList<string> toolNames,
        CancellationToken ct)
    {
        var tool = RepositoryToolNames.PickRepositoryListTool(toolNames);
        if (tool is null) return [];

        try
        {
            var response = await client.CallToolAsync(
                tool, new Dictionary<string, object?>(), cancellationToken: ct);

            if (response.IsError == true) return [];

            return [.. ReadNames(response).Take(MaxRepositoriesReported)];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogInformation(exception,
                "The '{Tool}' tool could not be read; reporting tools only", tool);

            return [];
        }
    }

    /// <summary>
    /// The same three shapes the search results accept, for the same reason:
    /// MCP does not say how a tool returns a list, so structured content, that
    /// JSON inside a text block, and plain lines are all honoured.
    /// </summary>
    private static IEnumerable<string> ReadNames(CallToolResult response)
    {
        if (response.StructuredContent is { } structured)
        {
            var fromStructured = NamesIn(structured).ToList();
            if (fromStructured.Count > 0) return fromStructured;
        }

        var names = new List<string>();

        foreach (var block in response.Content.OfType<TextContentBlock>())
        {
            if (string.IsNullOrWhiteSpace(block.Text)) continue;

            try
            {
                using var parsed = JsonDocument.Parse(block.Text);
                names.AddRange(NamesIn(parsed.RootElement));
            }
            catch (JsonException)
            {
                // Prose. One repository per line is the only reading that is
                // ever right, and a wrong one costs nothing here.
                names.AddRange(block.Text.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return names;
    }

    private static IEnumerable<string> NamesIn(JsonElement payload)
    {
        var array = payload;

        // Either the array itself, or an object wrapping it under a name a
        // server might reasonably have chosen.
        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in new[] { "repositories", "repos", "results", "items" })
            {
                if (payload.TryGetProperty(property, out var candidate)
                    && candidate.ValueKind == JsonValueKind.Array)
                {
                    array = candidate;
                    break;
                }
            }
        }

        if (array.ValueKind != JsonValueKind.Array) yield break;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                if (element.GetString() is { Length: > 0 } text) yield return text.Trim();
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object) continue;

            foreach (var property in new[] { "name", "repository", "repo", "path", "id" })
            {
                if (element.TryGetProperty(property, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text)
                {
                    yield return text.Trim();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The weaker probe, reached only when MCP failed. Any HTTP response counts
    /// — the question is "did something answer", not "did it like this
    /// request", and an MCP server has no reason to serve anything at its root.
    /// </summary>
    private async Task<EndpointProbeResult> ProbeHttpAsync(
        Uri uri,
        Exception mcpFailure,
        CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, ct);

            return EndpointProbeResult.NotMcp(
                $"Something answered ({(int)response.StatusCode} {response.ReasonPhrase}), but the "
                + $"MCP handshake failed: {mcpFailure.Message}. Check this is the MCP endpoint "
                + "rather than the service's home page.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return EndpointProbeResult.Failed(
                "Timed out. The host may be firewalled or the port wrong.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Probe of {Endpoint} failed", uri);

            return EndpointProbeResult.Failed($"Could not connect ({exception.Message}).");
        }
    }
}
