using System.ComponentModel;
using DocHub.Integrations.Knowledge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace DocHub.Integrations.Tests;

/// <summary>
/// The MCP repository source, against a real MCP server hosted in this process.
///
/// A real server rather than a mocked client, for the same reason the database
/// tests use real Postgres: what is worth testing here is whether we speak the
/// protocol correctly, and a fake client would only confirm that our own
/// assumptions agree with themselves. Only the repository behind the server is
/// scripted.
/// </summary>
public sealed class McpRepositorySourceTests
{
    [Fact]
    public async Task Passages_come_back_as_external_citations_with_their_own_locations()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var source = SourceFor(server.Endpoint);

        var result = await source.SearchAsync(new KnowledgeQuery("restart the worker", null, 5));

        var passage = result.Results.First();

        Assert.Equal(KnowledgeResultKind.External, passage.Kind);
        Assert.Equal("src/Worker/IngestionWorker.cs", passage.Title);
        Assert.Equal("lines 40-58", passage.Heading);
        Assert.Equal(
            "https://git.example.org/hub/blob/abc123/src/Worker/IngestionWorker.cs#L40-L58",
            passage.Url);

        // No document id, which is the whole reason the citation model was
        // widened before this source existed.
        Assert.Null(passage.DocumentId);

        // Verbatim. The assistant cites what it is given, so a source that
        // summarised would have it quoting text that exists nowhere.
        Assert.Equal(FakeRepositoryTools.WorkerSnippet, passage.Text);
    }

    [Fact]
    public async Task Two_results_get_different_ids_so_neither_is_deduplicated_away()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var result = await SourceFor(server.Endpoint)
            .SearchAsync(new KnowledgeQuery("restart the worker", null, 5));

        Assert.Equal(2, result.Results.Count);

        // Zero for everything would make the composite treat two unrelated files
        // as one passage and drop one of them.
        Assert.Equal(2, result.Results.Select(passage => passage.ChunkId).Distinct().Count());
    }

    [Fact]
    public async Task A_connected_server_reports_the_tool_it_is_searching_with()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var status = await SourceFor(server.Endpoint).CheckStatusAsync();

        Assert.Equal(KnowledgeSourceState.Active, status.State);
        Assert.Contains("search_code", status.Detail);
    }

    [Fact]
    public async Task With_no_tool_named_one_is_discovered_by_its_name()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var status = await SourceFor(server.Endpoint, toolName: "").CheckStatusAsync();

        Assert.Equal(KnowledgeSourceState.Active, status.State);

        // Deliberately not asserting *which*: the server exposes two matching
        // tools and nothing orders them, so pinning one would be testing the
        // SDK's enumeration order rather than our behaviour. That ambiguity is
        // why naming the tool is the supported path.
        Assert.Contains("search", status.Detail);
    }

    [Fact]
    public async Task An_address_nobody_set_is_inactive_rather_than_unavailable()
    {
        var status = await SourceFor(endpoint: null).CheckStatusAsync();

        // Off by design must not render like an outage — a permanent red light
        // is one users learn to ignore.
        Assert.Equal(KnowledgeSourceState.Inactive, status.State);
    }

    [Fact]
    public async Task An_address_that_does_not_answer_is_unavailable_and_names_itself()
    {
        // Reserved-for-documentation port range, so nothing is listening.
        const string dead = "http://127.0.0.1:9";

        var status = await SourceFor(dead).CheckStatusAsync();

        Assert.Equal(KnowledgeSourceState.Unavailable, status.State);
        Assert.Contains(dead, status.Detail);
    }

    [Fact]
    public async Task A_source_switched_off_contributes_nothing_without_being_asked()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var source = SourceFor(server.Endpoint, isEnabled: false);

        Assert.Empty((await source.SearchAsync(new KnowledgeQuery("anything", null, 5))).Results);
        Assert.Equal(KnowledgeSourceState.Inactive, (await source.CheckStatusAsync()).State);
    }

    [Fact]
    public async Task A_tool_that_answers_in_prose_still_grounds_an_answer()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var source = SourceFor(server.Endpoint, toolName: "search_notes");

        var result = await source.SearchAsync(new KnowledgeQuery("anything", null, 5));

        var passage = Assert.Single(result.Results);

        // Not every server returns the documented shape. Prose is still verbatim
        // text, so it can still ground an answer — it just cannot be located
        // more precisely than "the tool said this".
        Assert.Equal(FakeRepositoryTools.PlainNote, passage.Text);
        Assert.Equal(KnowledgeResultKind.External, passage.Kind);
        Assert.Null(passage.Url);
    }

    [Fact]
    public async Task A_tool_name_that_does_not_exist_says_which_ones_do()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var source = SourceFor(server.Endpoint, toolName: "grep_everything");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.SearchAsync(new KnowledgeQuery("anything", null, 5)));

        // Naming the available tools is what turns "it does not work" into a
        // configuration fix.
        Assert.Contains("search_code", failure.Message);
    }

    /// <summary>The name this test's source is declared under.</summary>
    private const string SourceName = "repositories";

    /// <summary>
    /// Names the tool by default. The scripted server exposes two whose names
    /// contain "search", so discovery would be a coin toss — which is the point
    /// of <see cref="RepositorySourceOptions.ToolName"/>, and is exercised on
    /// its own below.
    /// </summary>
    private static McpRepositoryKnowledgeSource SourceFor(
        string? endpoint,
        bool isEnabled = true,
        string toolName = "search_code")
    {
        var declared = new RepositorySourceOptions
        {
            Name = SourceName,
            DisplayName = "Repositories",
            Endpoint = endpoint,
            ToolName = toolName,
        };

        return new McpRepositoryKnowledgeSource(
            declared,
            new StubSettings(new RepositorySourceState(
                SourceName, "Repositories", endpoint, isEnabled, IsFromConfiguration: true)),
            Options.Create(new KnowledgeSourceOptions
            {
                RepositoryProvider = KnowledgeSourceOptions.McpProvider,
                Repositories = [declared],
            }),
            NullLoggerFactory.Instance,
            NullLogger<McpRepositoryKnowledgeSource>.Instance);
    }

    private sealed class StubSettings(RepositorySourceState state) : IRepositorySourceSettings
    {
        public Task<RepositorySourceState?> GetAsync(
            string name,
            CancellationToken ct = default) =>
            Task.FromResult<RepositorySourceState?>(state.Name == name ? state : null);

        public Task<IReadOnlyList<RepositorySourceState>> ListAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RepositorySourceState>>([state]);
    }

    /// <summary>An MCP server on a loopback port, torn down with the test.</summary>
    private sealed class FakeMcpServer : IAsyncDisposable
    {
        private readonly WebApplication app;

        private FakeMcpServer(WebApplication app, string endpoint)
        {
            this.app = app;
            Endpoint = endpoint;
        }

        public string Endpoint { get; }

        public static async Task<FakeMcpServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            // Port 0: the OS picks a free one, so parallel test runs cannot
            // collide on a hard-coded port.
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithTools<FakeRepositoryTools>();

            var app = builder.Build();
            app.MapMcp();

            await app.StartAsync();

            var address = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .First();

            return new FakeMcpServer(app, address);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

/// <summary>
/// A scripted repository. Returns the documented tool shape — results carrying
/// a path, a line range, a link and the matching source verbatim.
/// </summary>
[McpServerToolType]
public sealed class FakeRepositoryTools
{
    public const string WorkerSnippet =
        "if (exitCode != 0)\n{\n    _supervisor.Restart(worker);\n}";

    [McpServerTool(Name = "search_code")]
    [Description("Searches the team's repositories.")]
    public static SearchResponse SearchCode(string query, int maxResults) =>
        new(
        [
            new SearchHit(
                "src/Worker/IngestionWorker.cs",
                "lines 40-58",
                WorkerSnippet,
                "https://git.example.org/hub/blob/abc123/src/Worker/IngestionWorker.cs#L40-L58",
                0.91),
            new SearchHit(
                "docs/operations.md",
                "lines 12-20",
                "Restart the ingestion worker with `systemctl restart dochub-worker`.",
                "https://git.example.org/hub/blob/abc123/docs/operations.md#L12-L20",
                0.72),
        ]);

    public const string PlainNote =
        "The worker is supervised by systemd and restarts on a non-zero exit.";

    /// <summary>A server that answers in prose rather than the documented shape.</summary>
    [McpServerTool(Name = "search_notes")]
    [Description("Searches loose notes, returning prose.")]
    public static string SearchNotes(string query, int maxResults) => PlainNote;

    public sealed record SearchResponse(IReadOnlyList<SearchHit> Results);

    public sealed record SearchHit(
        string Path,
        string Lines,
        string Text,
        string Url,
        double Score);
}
