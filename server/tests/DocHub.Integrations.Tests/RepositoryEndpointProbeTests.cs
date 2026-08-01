using System.ComponentModel;
using DocHub.Integrations.Knowledge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace DocHub.Integrations.Tests;

/// <summary>
/// Testing an address before saving it, against a real MCP server in this
/// process.
///
/// What is worth asserting is that the probe answers the questions somebody
/// actually has at that moment: is this the right server, and is the tool
/// called what I assumed. An HTTP ping answers neither.
/// </summary>
public sealed class RepositoryEndpointProbeTests
{
    private static McpRepositoryEndpointProbe Probe() =>
        new(
            new HttpClient { Timeout = TimeSpan.FromSeconds(5) },
            NullLoggerFactory.Instance,
            NullLogger<McpRepositoryEndpointProbe>.Instance);

    [Fact]
    public async Task A_real_server_reports_its_tools_and_what_it_indexes()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var result = await Probe().ProbeAsync(server.Endpoint);

        Assert.True(result.IsReachable);
        Assert.True(result.SpeaksMcp);

        // The tool list is the point: it is what settles "is the search tool
        // called what we assumed" without anyone opening a shell.
        Assert.Contains("search_code", result.Tools);
        Assert.Contains("list_repos", result.Tools);

        // And what the server indexes, which is how two servers exposing
        // identical tools are told apart.
        Assert.Equal(FakeRepositoryTools.Repositories, result.Repositories);
        Assert.Contains("3 repositories", result.Detail);
    }

    [Fact]
    public async Task The_suggested_tool_is_the_one_searching_would_pick()
    {
        await using var server = await FakeMcpServer.StartAsync();

        var result = await Probe().ProbeAsync(server.Endpoint);

        // Not asserting *which*: the server exposes two tools with "search" in
        // the name and nothing orders them. What matters is that the suggestion
        // comes from the same rule the searcher uses, so the screen cannot
        // offer a tool that would then not be used.
        Assert.NotNull(result.SuggestedToolName);
        Assert.Contains("search", result.SuggestedToolName);
        Assert.Contains(result.SuggestedToolName, result.Tools);
        Assert.Contains(result.SuggestedToolName, result.Detail);
    }

    [Fact]
    public async Task A_server_with_nothing_searchable_says_so_rather_than_passing()
    {
        await using var server = await FakeMcpServer.StartAsync<AnswerOnlyTools>();

        var result = await Probe().ProbeAsync(server.Endpoint);

        // Reachable, speaks MCP, and useless for grounding: nothing here
        // returns source text to cite. Calling that a pass would send somebody
        // away with a source that fails on every question.
        Assert.True(result.SpeaksMcp);
        Assert.Null(result.SuggestedToolName);
        Assert.Contains("none of its", result.Detail);
        Assert.Contains("get_answer", result.Tools);
    }

    [Fact]
    public async Task A_web_server_that_is_not_MCP_is_reachable_and_not_usable()
    {
        await using var plain = await PlainWebServer.StartAsync();

        var result = await Probe().ProbeAsync(plain.Endpoint);

        // The distinction that matters: something is listening, so the address
        // and the network path are right, and it still cannot be used. Told
        // apart from "nothing answered", which needs a different fix.
        Assert.True(result.IsReachable);
        Assert.False(result.SpeaksMcp);
        Assert.Contains("MCP handshake failed", result.Detail);
        Assert.Empty(result.Tools);
    }

    [Fact]
    public async Task An_address_with_nothing_listening_is_not_reachable()
    {
        // Reserved-for-documentation port, so nothing is listening.
        var result = await Probe().ProbeAsync("http://127.0.0.1:9");

        Assert.False(result.IsReachable);
        Assert.False(result.SpeaksMcp);
    }

    [Theory]
    // Parses as an absolute URI whose scheme is "mcp.internal" — the trap that
    // makes "is it absolute" the wrong question to ask.
    [InlineData("mcp.internal:8080")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url at all")]
    public async Task Anything_that_is_not_an_http_address_fails_before_any_request(string address)
    {
        var result = await Probe().ProbeAsync(address);

        Assert.False(result.IsReachable);
        Assert.Contains("http://", result.Detail);
    }

    /// <summary>
    /// A server whose only tool synthesises an answer — the shape the org's own
    /// servers offer alongside search, and the one that must never be picked:
    /// the assistant cites what it is handed, so prose that exists in no file
    /// would be quoted as though it did.
    /// </summary>
    [McpServerToolType]
    public sealed class AnswerOnlyTools
    {
        [McpServerTool(Name = "get_answer")]
        [Description("Answers a question about the codebase in its own words.")]
        public static string GetAnswer(string question) =>
            "The worker restarts automatically on a non-zero exit.";
    }

    /// <summary>An ordinary web server: answers HTTP, knows nothing of MCP.</summary>
    private sealed class PlainWebServer : IAsyncDisposable
    {
        private readonly WebApplication app;

        private PlainWebServer(WebApplication app, string endpoint)
        {
            this.app = app;
            Endpoint = endpoint;
        }

        public string Endpoint { get; }

        public static async Task<PlainWebServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            var app = builder.Build();
            app.MapGet("/", () => Results.Text("It works!"));
            app.MapPost("/", () => Results.Text("It works!"));

            await app.StartAsync();

            var address = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .First();

            return new PlainWebServer(app, address);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
