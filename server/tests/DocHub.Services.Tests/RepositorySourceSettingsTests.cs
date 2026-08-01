using DocHub.Integrations.Knowledge;
using DocHub.Services.Knowledge;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Tests;

/// <summary>
/// How each administrator's stored setting and the deployment's configuration
/// combine.
///
/// The rule under test is that an override wins if there is one, and
/// configuration applies otherwise — so a deployment keeps a baseline it can
/// rely on, while day-to-day changes are a UI edit rather than an app-pool
/// variable and a restart.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class RepositorySourceSettingsTests(StackFixture fixture)
{
    private const string Primary = "repositories";
    private const string Secondary = "implementations";

    /// <summary>
    /// Configuration declaring the two sources these tests use. Declaring a
    /// source is what makes it exist at all — an override for an undeclared
    /// name is ignored.
    /// </summary>
    private static KnowledgeSourceOptions Declaring(
        string provider = KnowledgeSourceOptions.NoneProvider,
        string? primaryEndpoint = null,
        string? secondaryEndpoint = null) =>
        new()
        {
            RepositoryProvider = provider,
            Repositories =
            [
                new RepositorySourceOptions
                {
                    Name = Primary,
                    DisplayName = "Repositories",
                    Endpoint = primaryEndpoint,
                },
                new RepositorySourceOptions
                {
                    Name = Secondary,
                    DisplayName = "Implementations",
                    Endpoint = secondaryEndpoint,
                },
            ],
        };

    private static RepositorySourceSettings Reading(
        StackFixture.Scope scope,
        KnowledgeSourceOptions? configured = null) =>
        new(scope.SourceSettings, Options.Create(configured ?? Declaring()));

    /// <summary>Both overrides gone, so each test starts from configuration alone.</summary>
    private static async Task ClearOverridesAsync(StackFixture.Scope scope)
    {
        await scope.SourceSettings.ClearAsync(Primary);
        await scope.SourceSettings.ClearAsync(Secondary);
    }

    [Fact]
    public async Task With_nothing_stored_or_configured_the_source_has_no_address()
    {
        await using var scope = fixture.NewScope();
        await ClearOverridesAsync(scope);

        var state = await Reading(scope).GetAsync(Primary);

        Assert.NotNull(state);
        Assert.Null(state.Endpoint);
        Assert.False(state.IsEnabled);
        Assert.True(state.IsFromConfiguration);
    }

    [Fact]
    public async Task An_endpoint_saved_by_an_administrator_takes_effect()
    {
        await using var scope = fixture.NewScope();

        await scope.SourceSettings.SaveAsync(
            Primary, "http://mcp.internal:8080", isEnabled: true, updatedById: scope.User.Id);

        var state = await Reading(scope).GetAsync(Primary);

        Assert.Equal("http://mcp.internal:8080", state!.Endpoint);
        Assert.True(state.IsEnabled);

        // The screen says which one is in effect — an administrator editing a
        // field that configuration is overriding would otherwise have no way to
        // tell why nothing changed.
        Assert.False(state.IsFromConfiguration);
    }

    [Fact]
    public async Task A_stored_setting_wins_over_configuration()
    {
        await using var scope = fixture.NewScope();

        await scope.SourceSettings.SaveAsync(
            Primary, "http://chosen-in-the-ui:8080", isEnabled: true, updatedById: scope.User.Id);

        var state = await Reading(scope, Declaring(
            KnowledgeSourceOptions.McpProvider,
            primaryEndpoint: "http://declared-in-config:9999")).GetAsync(Primary);

        Assert.Equal("http://chosen-in-the-ui:8080", state!.Endpoint);
    }

    [Fact]
    public async Task Clearing_the_override_hands_control_back_to_configuration()
    {
        await using var scope = fixture.NewScope();

        await scope.SourceSettings.SaveAsync(
            Primary, "http://chosen-in-the-ui:8080", isEnabled: true, updatedById: scope.User.Id);

        await scope.SourceSettings.ClearAsync(Primary);

        var state = await Reading(scope, Declaring(
            KnowledgeSourceOptions.McpProvider,
            primaryEndpoint: "http://declared-in-config:9999")).GetAsync(Primary);

        // Removing the override is deliberately different from saving an empty
        // one: the first restores the deployment's baseline, the second is an
        // administrator switching the source off.
        Assert.Equal("http://declared-in-config:9999", state!.Endpoint);
        Assert.True(state.IsFromConfiguration);
    }

    [Fact]
    public async Task An_endpoint_left_in_configuration_does_not_re_enable_a_none_provider()
    {
        await using var scope = fixture.NewScope();
        await ClearOverridesAsync(scope);

        // The realistic mistake: someone switches the provider back to "none"
        // and leaves the address behind. That must not count as configured.
        var state = await Reading(scope, Declaring(
            KnowledgeSourceOptions.NoneProvider,
            primaryEndpoint: "http://left-behind:9999")).GetAsync(Primary);

        Assert.Null(state!.Endpoint);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public async Task Switching_the_source_off_keeps_its_address()
    {
        await using var scope = fixture.NewScope();

        await scope.SourceSettings.SaveAsync(
            Primary, "http://mcp.internal:8080", isEnabled: false, updatedById: scope.User.Id);

        var state = await Reading(scope).GetAsync(Primary);

        // Turning a source off during an outage must not throw away the address
        // it will need back.
        Assert.Equal("http://mcp.internal:8080", state!.Endpoint);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public async Task Each_source_carries_its_own_address()
    {
        await using var scope = fixture.NewScope();
        await ClearOverridesAsync(scope);

        // Only one of the two is overridden. The other must keep the address
        // configuration gave it rather than inheriting its neighbour's — they
        // index different code, and answering from the wrong one is worse than
        // answering from neither.
        await scope.SourceSettings.SaveAsync(
            Secondary, "http://impl.internal:8080", isEnabled: true, updatedById: scope.User.Id);

        var settings = Reading(scope, Declaring(
            KnowledgeSourceOptions.McpProvider,
            primaryEndpoint: "http://cs.internal:9999",
            secondaryEndpoint: "http://impl-from-config:9999"));

        var primary = await settings.GetAsync(Primary);
        var secondary = await settings.GetAsync(Secondary);

        Assert.Equal("http://cs.internal:9999", primary!.Endpoint);
        Assert.True(primary.IsFromConfiguration);

        Assert.Equal("http://impl.internal:8080", secondary!.Endpoint);
        Assert.False(secondary.IsFromConfiguration);
    }

    [Fact]
    public async Task Listing_returns_every_declared_source_in_configuration_order()
    {
        await using var scope = fixture.NewScope();
        await ClearOverridesAsync(scope);

        var states = await Reading(scope).ListAsync();

        Assert.Collection(
            states,
            first => Assert.Equal(Primary, first.Name),
            second => Assert.Equal(Secondary, second.Name));

        // The display name travels with it: it is what names the source in the
        // sentence saying which one could not be searched.
        Assert.Equal("Implementations", states[1].DisplayName);
    }

    [Fact]
    public async Task A_source_configuration_does_not_declare_is_unknown()
    {
        await using var scope = fixture.NewScope();

        // An override left behind by a renamed or retired server. Honouring it
        // would put a source on the screen no deployment asked for.
        await scope.SourceSettings.SaveAsync(
            "retired", "http://gone:8080", isEnabled: true, updatedById: scope.User.Id);

        var settings = Reading(scope);

        Assert.Null(await settings.GetAsync("retired"));
        Assert.DoesNotContain(await settings.ListAsync(), state => state.Name == "retired");

        await scope.SourceSettings.ClearAsync("retired");
    }

    [Fact]
    public async Task The_sources_screen_explains_why_repositories_are_not_searched()
    {
        await using var scope = fixture.NewScope();

        var sources = await scope.Knowledge.DescribeSourcesAsync();
        var repositories = Assert.Single(sources, source => source.Name == "repositories");

        // Inactive rather than unavailable: nothing is broken, and a permanent
        // red light is one users learn to ignore.
        Assert.Equal("inactive", repositories.State);
        Assert.Contains("configured", repositories.Detail);
    }
}
