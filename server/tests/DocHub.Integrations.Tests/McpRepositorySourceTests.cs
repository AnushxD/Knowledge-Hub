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
    public async Task With_no_tool_named_every_tool_that_can_be_asked_is_named()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var status = await SourceFor(server.Endpoint, toolName: "").CheckStatusAsync();

        Assert.Equal(KnowledgeSourceState.Active, status.State);

        // Both search tools, rather than whichever the SDK happened to list
        // first: which one that was used to decide the whole source's answer.
        Assert.Contains("search_code", status.Detail);
        Assert.Contains("search_notes", status.Detail);

        // And not the one there is nowhere to put a question in.
        Assert.DoesNotContain("list_repos", status.Detail);
    }

    [Fact]
    public async Task Every_tool_the_question_can_be_put_to_contributes_passages()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var result = await SourceFor(server.Endpoint, toolName: "")
            .SearchAsync(new KnowledgeQuery("restart the worker", null, 8));

        var texts = result.Results.Select(passage => passage.Text).ToList();

        // The structured hits from search_code and the prose from search_notes,
        // in one answer. Under one-tool-only whichever tool lost the coin toss
        // contributed nothing, and a server's best answer is not reliably in
        // the tool whose name happens to sort first.
        Assert.Contains(FakeRepositoryTools.WorkerSnippet, texts);
        Assert.Contains(FakeRepositoryTools.PlainNote, texts);

        Assert.Null(result.Degradation);
    }

    [Fact]
    public async Task A_tool_with_nowhere_to_put_the_question_is_never_called()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var result = await SourceFor(server.Endpoint, toolName: "")
            .SearchAsync(new KnowledgeQuery("restart the worker", null, 8));

        // list_repos takes no arguments, so it answers the same thing however
        // it is asked — which is noise on every answer rather than grounding.
        // A passage from it would carry its name, since that is where a tool
        // answering in prose is recorded.
        Assert.DoesNotContain(result.Results, passage => passage.Heading == "list_repos");
    }

    [Fact]
    public async Task A_tool_that_changes_something_is_never_called()
    {
        await using var server = await FakeMcpServer.StartAsync<MutatingTools>();

        var result = await SourceFor(server.Endpoint, toolName: "")
            .SearchAsync(new KnowledgeQuery("stale branches", null, 5));

        // Both take a string and would happily be "asked" — one says so in its
        // annotations, the other only in its name. Asking either would turn a
        // question into a write into somebody's repository, which is the one
        // thing this hub must never do.
        Assert.Empty(MutatingTools.Called);

        var passage = Assert.Single(result.Results);
        Assert.Equal(MutatingTools.Finding, passage.Text);
    }

    /// <summary>
    /// A server whose tools are not all safe to call. Records what was asked,
    /// because the assertion worth making is about the call, not the answer.
    /// </summary>
    [McpServerToolType]
    public sealed class MutatingTools
    {
        public const string Finding = "Branch policy: stale branches are pruned after 90 days.";

        public static readonly List<string> Called = [];

        [McpServerTool(Name = "search_policies")]
        [Description("The only tool here that reads.")]
        public static Response SearchPolicies(string query) => new([new Hit(Finding)]);

        /// <summary>Declares itself, which is what a well-behaved server does.</summary>
        [McpServerTool(Name = "prune_branches", ReadOnly = false, Destructive = true)]
        [Description("Deletes stale branches.")]
        public static string PruneBranches(string query)
        {
            Called.Add("prune_branches");
            return "Pruned.";
        }

        /// <summary>Declares nothing, so only the name gives it away.</summary>
        [McpServerTool(Name = "delete_snapshot")]
        [Description("Removes a snapshot, and admits nothing about it.")]
        public static string DeleteSnapshot(string query)
        {
            Called.Add("delete_snapshot");
            return "Deleted.";
        }

        public sealed record Response(IReadOnlyList<Hit> Results);

        public sealed record Hit(string Text);
    }

    [Fact]
    public async Task One_tool_failing_degrades_the_answer_instead_of_losing_it()
    {
        await using var server = await FakeMcpServer.StartAsync<HalfBrokenTools>();

        var result = await SourceFor(server.Endpoint, toolName: "")
            .SearchAsync(new KnowledgeQuery("worker", null, 5));

        // The same rule the composite applies across sources, applied within
        // one: what still works is used, and what did not is named on the
        // answer rather than logged and forgotten.
        Assert.Equal(HalfBrokenTools.Finding, Assert.Single(result.Results).Text);

        Assert.NotNull(result.Degradation);
        Assert.Contains("search_broken", result.Degradation);
    }

    [Fact]
    public async Task Every_tool_failing_is_the_source_failing()
    {
        await using var server = await FakeMcpServer.StartAsync<BrokenTools>();

        // Reported as an exception, not as an empty answer: the composite
        // isolates each source, and "no results" would hide a broken server
        // behind an answer that merely looks thin.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SourceFor(server.Endpoint, toolName: "")
                .SearchAsync(new KnowledgeQuery("worker", null, 5)));

        Assert.Contains("search_broken", failure.Message);
    }

    /// <summary>One tool that answers and one that throws.</summary>
    [McpServerToolType]
    public sealed class HalfBrokenTools
    {
        public const string Finding = "The worker restarts on a non-zero exit.";

        [McpServerTool(Name = "search_working")]
        [Description("Answers normally.")]
        public static Response SearchWorking(string query) => new([new Hit(Finding)]);

        [McpServerTool(Name = "search_broken")]
        [Description("Throws every time.")]
        public static string SearchBroken(string query) =>
            throw new InvalidOperationException("The index is rebuilding.");

        public sealed record Response(IReadOnlyList<Hit> Results);

        public sealed record Hit(string Text);
    }

    /// <summary>A server where nothing works.</summary>
    [McpServerToolType]
    public sealed class BrokenTools
    {
        [McpServerTool(Name = "search_broken")]
        [Description("Throws every time.")]
        public static string SearchBroken(string query) =>
            throw new InvalidOperationException("The index is rebuilding.");
    }

    [Fact]
    public async Task A_server_with_nothing_that_can_be_asked_says_so()
    {
        await using var server = await FakeMcpServer.StartAsync<UnaskableTools>();

        var status = await SourceFor(server.Endpoint, toolName: "").CheckStatusAsync();

        // Reachable, speaks MCP, and still cannot ground anything. A different
        // problem from an outage, and the detail has to be actionable rather
        // than merely red.
        Assert.Equal(KnowledgeSourceState.Unavailable, status.State);
        Assert.Contains("no read-only tool", status.Detail);
        Assert.Contains("purge_cache", status.Detail);
    }

    /// <summary>Everything here either changes something or takes no query.</summary>
    [McpServerToolType]
    public sealed class UnaskableTools
    {
        [McpServerTool(Name = "purge_cache")]
        [Description("Empties the index cache.")]
        public static string PurgeCache(string scope) => "Purged.";

        [McpServerTool(Name = "health")]
        [Description("Reports whether the server is well.")]
        public static string Health() => "ok";
    }

    [Fact]
    public void A_repository_server_carries_no_description_of_its_own()
    {
        // Its display name says which server it is and the status line under it
        // names the address and the tool — both true of this one only. A
        // sentence in between would read the same on every server, so the
        // screen renders nothing rather than filler.
        Assert.Empty(SourceFor("http://mcp.internal:8080").Description);
    }

    [Fact]
    public async Task A_server_switched_off_is_inactive_rather_than_unavailable()
    {
        // A reachable address, switched off by an administrator. Nothing is
        // wrong, so nothing should look wrong — a permanent red light is one
        // users learn to ignore. The address must survive, too, or turning it
        // back on means retyping it.
        var status = await SourceFor("http://127.0.0.1:9", isEnabled: false).CheckStatusAsync();

        Assert.Equal(KnowledgeSourceState.Inactive, status.State);
        Assert.Contains("127.0.0.1:9", status.Detail);
    }

    [Fact]
    public async Task A_server_switched_off_is_not_searched()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var result = await SourceFor(server.Endpoint, isEnabled: false)
            .SearchAsync(new KnowledgeQuery("vpn", null, 5));

        // Empty rather than an exception: the composite reports exceptions as
        // degradations, and a deliberate switch-off degrades nothing.
        Assert.Empty(result.Results);
        Assert.Null(result.Degradation);
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
        // more precisely than "this server said this".
        Assert.Equal(FakeRepositoryTools.PlainNote, passage.Text);
        Assert.Equal(KnowledgeResultKind.External, passage.Kind);
        Assert.Null(passage.Url);

        // Titled with the server, not the tool. "search_notes" means nothing to
        // someone deciding whether to trust the sentence it supports.
        Assert.Equal("Repositories", passage.Title);
    }

    [Fact]
    public async Task A_hit_with_no_path_is_cited_to_the_server_rather_than_the_tool()
    {
        await using var server = await FakeMcpServer.StartAsync<PathlessTools>();

        var source = SourceFor(server.Endpoint, toolName: "search_anything");

        var passage = Assert.Single(
            (await source.SearchAsync(new KnowledgeQuery("anything", null, 5))).Results);

        Assert.Equal("Repositories", passage.Title);
        Assert.Equal("Something worth citing.", passage.Text);
    }

    /// <summary>A server returning the documented shape, minus the path.</summary>
    [McpServerToolType]
    public sealed class PathlessTools
    {
        [McpServerTool(Name = "search_anything")]
        [Description("Returns a hit that names no file.")]
        public static Response SearchAnything(string query, int maxResults) =>
            new([new Hit("Something worth citing.")]);

        public sealed record Response(IReadOnlyList<Hit> Results);

        public sealed record Hit(string Text);
    }

    [Fact]
    public async Task A_server_saying_nothing_matched_contributes_nothing()
    {
        await using var server = await FakeMcpServer.StartAsync<EmptyResultTools>();

        var result = await SourceFor(server.Endpoint, toolName: "search_empty")
            .SearchAsync(new KnowledgeQuery("how to make orange juice", null, 5));

        // The shape was understood and it said no matches. Reading that as one
        // passage of prose — the raw JSON, offered as something citable — is
        // what let an unrelated server ground an answer.
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task A_results_envelope_is_recognised_whatever_the_server_calls_it()
    {
        await using var server = await FakeMcpServer.StartAsync<SingularResultTools>();

        var passage = Assert.Single(
            (await SourceFor(server.Endpoint, toolName: "search_singular")
                .SearchAsync(new KnowledgeQuery("worker", null, 5))).Results);

        // "result" rather than "results", which is what one real server uses.
        Assert.Equal("src/Worker.cs", passage.Title);
    }

    [Fact]
    public async Task Results_in_a_shape_we_cannot_take_apart_are_still_passed_through()
    {
        await using var server = await FakeMcpServer.StartAsync<UnfamiliarHitTools>();

        var passage = Assert.Single(
            (await SourceFor(server.Endpoint, toolName: "search_unfamiliar")
                .SearchAsync(new KnowledgeQuery("Arsenal", null, 5))).Results);

        // The server found something and described it in fields nothing here
        // recognises. Reporting that as no results would silently discard real
        // matches, so the whole reply survives as one passage — which is still
        // verbatim, just not locatable.
        Assert.Contains("Arsenal", passage.Text);
    }

    /// <summary>
    /// A server whose hits carry no text field — real results, unfamiliar
    /// shape. The live-scores server in use here answers like this.
    /// </summary>
    [McpServerToolType]
    public sealed class UnfamiliarHitTools
    {
        [McpServerTool(Name = "search_unfamiliar")]
        [Description("Answers with entries that have no text field.")]
        public static string SearchUnfamiliar(string query, int maxResults) =>
            $$"""
            Search results for '{{query}}':

            {"result": [{"name": "Arsenal", "country": "England", "type": "team"}]}
            """;
    }

    /// <summary>A server that understood the query and found nothing.</summary>
    [McpServerToolType]
    public sealed class EmptyResultTools
    {
        [McpServerTool(Name = "search_empty")]
        [Description("Finds nothing, and says so in the documented shape.")]
        public static Response SearchEmpty(string query, int maxResults) => new([]);

        public sealed record Response(IReadOnlyList<string> Result);
    }

    /// <summary>A server whose envelope is "result", not "results".</summary>
    [McpServerToolType]
    public sealed class SingularResultTools
    {
        [McpServerTool(Name = "search_singular")]
        [Description("Answers in the documented shape under a singular key.")]
        public static Response SearchSingular(string query, int maxResults) =>
            new([new Hit("src/Worker.cs", "The worker restarts on a non-zero exit.")]);

        public sealed record Response(IReadOnlyList<Hit> Result);

        public sealed record Hit(string Path, string Text);
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
        var descriptor = new RepositorySourceDescriptor(
            SourceName,
            "Repositories",
            endpoint ?? "http://unset.invalid",
            toolName,
            isEnabled);

        return new McpRepositoryKnowledgeSource(
            descriptor,
            Options.Create(new KnowledgeSourceOptions
            {
                RepositoryProvider = KnowledgeSourceOptions.McpProvider,
            }),
            NullLoggerFactory.Instance,
            NullLogger<McpRepositoryKnowledgeSource>.Instance);
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

    public static readonly string[] Repositories = ["hub", "worker", "docs"];

    /// <summary>
    /// What the org's servers offer alongside search. Nothing grounds an answer
    /// in it — it is how the "test address" button says which server this is.
    /// </summary>
    [McpServerTool(Name = "list_repos")]
    [Description("The repositories this server indexes.")]
    public static RepositoryList ListRepos() => new(Repositories);

    public sealed record RepositoryList(IReadOnlyList<string> Repositories);

    public sealed record SearchResponse(IReadOnlyList<SearchHit> Results);

    public sealed record SearchHit(
        string Path,
        string Lines,
        string Text,
        string Url,
        double Score);
}
