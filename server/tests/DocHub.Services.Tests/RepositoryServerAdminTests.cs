using DocHub.Integrations.Knowledge;
using DocHub.Services.Knowledge;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Tests;

/// <summary>
/// Managing MCP repository servers from the UI.
///
/// The behaviour that matters is that the table <b>is</b> the list of sources:
/// a server added here is searched by the next question, with no restart and
/// nothing to edit on the box. These run against real Postgres, because the
/// whole point is what the rows do.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class RepositoryServerAdminTests(StackFixture fixture)
{
    private static RepositorySourceAdmin Administering(StackFixture.Scope scope) =>
        new(
            scope.SourceSettings,
            new StubProbe(),
            scope.User,
            NullLogger<RepositorySourceAdmin>.Instance);

    /// <summary>
    /// The catalog as the composite uses it, over one deployment setting. This
    /// is what turns rows into searchable sources.
    /// </summary>
    private static KnowledgeSourceCatalog Cataloguing(
        StackFixture.Scope scope,
        string provider = KnowledgeSourceOptions.McpProvider) =>
        new(
            [],
            scope.SourceSettings,
            new StubSourceFactory(),
            Options.Create(new KnowledgeSourceOptions { RepositoryProvider = provider }));

    private static CreateRepositorySourceRequest NewServer(
        string name,
        string endpoint = "http://mcp.internal:8080") =>
        new()
        {
            Name = name,
            DisplayName = "Code search",
            Endpoint = endpoint,
            ToolName = "search_codebase",
            IsEnabled = true,
        };

    /// <summary>Leaves the table as each test found it — the scope is shared.</summary>
    private static async Task RemoveAllAsync(StackFixture.Scope scope)
    {
        foreach (var existing in await scope.SourceSettings.ListAsync())
            await scope.SourceSettings.DeleteAsync(existing.Name);
    }

    [Fact]
    public async Task A_server_added_in_the_UI_is_searched_without_a_restart()
    {
        await using var scope = fixture.NewScope();
        await RemoveAllAsync(scope);

        var admin = Administering(scope);
        await admin.CreateAsync(NewServer("code-search"));

        // The catalog was built before the row existed, and still finds it:
        // sources are resolved per question, not captured at startup.
        var resolved = await Cataloguing(scope).ResolveAsync();

        Assert.Single(resolved, source => source.Name == "code-search");
        await RemoveAllAsync(scope);
    }

    [Fact]
    public async Task Two_servers_are_two_sources()
    {
        await using var scope = fixture.NewScope();
        await RemoveAllAsync(scope);

        var admin = Administering(scope);
        await admin.CreateAsync(NewServer("code-search", "http://cs.internal:8080"));
        await admin.CreateAsync(NewServer("implementations", "http://impl.internal:8080"));

        var resolved = await Cataloguing(scope).ResolveAsync();

        Assert.Equal(
            ["code-search", "implementations"],
            resolved.Select(source => source.Name).OrderBy(name => name));

        await RemoveAllAsync(scope);
    }

    [Fact]
    public async Task A_name_that_is_already_taken_is_refused()
    {
        await using var scope = fixture.NewScope();
        await RemoveAllAsync(scope);

        var admin = Administering(scope);
        await admin.CreateAsync(NewServer("code-search"));

        // The name is the route key and the citation's attribution, so a clash
        // is a real conflict rather than something to paper over.
        var failure = await Assert.ThrowsAsync<ValidationException>(
            () => admin.CreateAsync(NewServer("code-search", "http://elsewhere:8080")));

        Assert.Contains("already exists", failure.Message);
        await RemoveAllAsync(scope);
    }

    [Theory]
    // Spaces and slashes would need escaping in the route it becomes.
    [InlineData("Code Search")]
    [InlineData("code/search")]
    [InlineData("")]
    public async Task A_name_that_would_not_survive_a_URL_is_refused(string name)
    {
        await using var scope = fixture.NewScope();

        await Assert.ThrowsAsync<ValidationException>(
            () => Administering(scope).CreateAsync(NewServer(name)));
    }

    [Theory]
    [InlineData("mcp.internal:8080")]
    [InlineData("file:///etc/passwd")]
    [InlineData("")]
    public async Task An_address_that_is_not_absolute_http_is_refused(string endpoint)
    {
        await using var scope = fixture.NewScope();

        // Pointing the server at an arbitrary host is admin-gated by design;
        // letting it read a file would be something else entirely.
        await Assert.ThrowsAsync<ValidationException>(
            () => Administering(scope).CreateAsync(NewServer("code-search", endpoint)));
    }

    [Fact]
    public async Task Switching_a_server_off_keeps_it_and_its_address()
    {
        await using var scope = fixture.NewScope();
        await RemoveAllAsync(scope);

        var admin = Administering(scope);
        var created = await admin.CreateAsync(NewServer("code-search"));

        var updated = await admin.UpdateAsync(created.Name, new UpdateRepositorySourceRequest
        {
            DisplayName = created.DisplayName,
            Endpoint = created.Endpoint,
            ToolName = created.ToolName,
            IsEnabled = false,
        });

        // Taking a server out of circulation during an outage must not throw
        // away the address it will need back.
        Assert.False(updated.IsEnabled);
        Assert.Equal(created.Endpoint, updated.Endpoint);

        // And it stays a source, so the screen can say it is off on purpose
        // rather than silently dropping it.
        Assert.Single(await Cataloguing(scope).ResolveAsync(), s => s.Name == "code-search");
        await RemoveAllAsync(scope);
    }

    [Fact]
    public async Task Removing_the_last_server_brings_back_the_placeholder()
    {
        await using var scope = fixture.NewScope();
        await RemoveAllAsync(scope);

        var admin = Administering(scope);
        await admin.CreateAsync(NewServer("code-search"));
        await admin.DeleteAsync("code-search");

        Assert.Empty(await admin.ListAsync());

        // Not an empty fan-out: the stand-in keeps the merge exercised and says
        // what would fill the gap.
        var resolved = await Cataloguing(scope).ResolveAsync();
        var placeholder = Assert.Single(resolved);

        Assert.Equal("repositories", placeholder.Name);
        Assert.Contains(
            "No repository servers have been added",
            (await placeholder.CheckStatusAsync()).Detail);
    }

    [Fact]
    public async Task Editing_or_removing_a_server_that_does_not_exist_is_a_404()
    {
        await using var scope = fixture.NewScope();
        var admin = Administering(scope);

        await Assert.ThrowsAsync<NotFoundException>(() => admin.GetAsync("never-added"));
        await Assert.ThrowsAsync<NotFoundException>(() => admin.DeleteAsync("never-added"));
    }

    [Fact]
    public async Task Out_of_the_box_a_server_added_in_the_UI_is_searched()
    {
        await using var scope = fixture.NewScope();
        await RemoveAllAsync(scope);

        // Deliberately the *default* options rather than a provider chosen here:
        // defaulting the switch off would mean a server added in the UI does
        // nothing until somebody edits a file, which is what the UI exists to
        // avoid. This is the assertion that stops that default drifting back.
        var catalog = new KnowledgeSourceCatalog(
            [],
            scope.SourceSettings,
            new StubSourceFactory(),
            Options.Create(new KnowledgeSourceOptions()));

        await Administering(scope).CreateAsync(NewServer("code-search"));

        Assert.Single(await catalog.ResolveAsync(), source => source.Name == "code-search");
        await RemoveAllAsync(scope);
    }

    [Fact]
    public async Task Repository_search_switched_off_leaves_the_servers_alone()
    {
        await using var scope = fixture.NewScope();
        await RemoveAllAsync(scope);

        var admin = Administering(scope);
        await admin.CreateAsync(NewServer("code-search"));

        var resolved = await Cataloguing(scope, KnowledgeSourceOptions.NoneProvider)
            .ResolveAsync();

        // The deployment's switch overrides the table without emptying it: the
        // row is still there when it is turned back on.
        var placeholder = Assert.Single(resolved);
        Assert.Equal("repositories", placeholder.Name);
        Assert.Contains("switched off", (await placeholder.CheckStatusAsync()).Detail);
        Assert.Single(await admin.ListAsync());

        await RemoveAllAsync(scope);
    }

    /// <summary>Reachable, always — these tests are about rows, not networks.</summary>
    private sealed class StubProbe : IRepositoryEndpointProbe
    {
        public Task<EndpointProbeResult> ProbeAsync(
            string endpoint,
            CancellationToken ct = default) =>
            Task.FromResult(new EndpointProbeResult(
                IsReachable: true,
                SpeaksMcp: true,
                $"{endpoint} answered.",
                ["search_codebase"],
                ["search_codebase"],
                "search_codebase",
                []));
    }

    /// <summary>
    /// Stands in for the MCP client: these tests are about which sources the
    /// catalog produces, not about talking to a server.
    /// </summary>
    private sealed class StubSourceFactory : IRepositoryKnowledgeSourceFactory
    {
        public IKnowledgeSource Create(RepositorySourceDescriptor source) =>
            new StubSource(source.Name, source.DisplayName, "Built from a row.");

        public IKnowledgeSource CreatePlaceholder(string detail) =>
            new StubSource("repositories", "Repositories", detail);
    }

    private sealed class StubSource(string name, string displayName, string detail)
        : IKnowledgeSource
    {
        public string Name => name;

        public string DisplayName => displayName;

        public string Description => "A source that exists only inside this test.";

        public Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new KnowledgeSourceStatus(KnowledgeSourceState.Inactive, detail));

        public Task<KnowledgeSearchResult> SearchAsync(
            KnowledgeQuery query,
            CancellationToken ct = default) =>
            Task.FromResult(KnowledgeSearchResult.Empty);
    }
}
