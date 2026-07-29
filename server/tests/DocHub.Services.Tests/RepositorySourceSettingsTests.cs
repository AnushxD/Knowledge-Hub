using DocHub.Integrations.Knowledge;
using DocHub.Services.Knowledge;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Tests;

/// <summary>
/// How the administrator's stored setting and the deployment's configuration
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
    private static RepositorySourceSettings Reading(
        StackFixture.Scope scope,
        KnowledgeSourceOptions? configured = null) =>
        new(scope.SourceSettings, Options.Create(configured ?? new KnowledgeSourceOptions()));

    [Fact]
    public async Task With_nothing_stored_or_configured_the_source_has_no_address()
    {
        await using var scope = fixture.NewScope();
        await scope.SourceSettings.ClearAsync(RepositorySourceSettings.SourceName);

        var state = await Reading(scope).GetAsync();

        Assert.Null(state.Endpoint);
        Assert.False(state.IsEnabled);
        Assert.True(state.IsFromConfiguration);
    }

    [Fact]
    public async Task An_endpoint_saved_by_an_administrator_takes_effect()
    {
        await using var scope = fixture.NewScope();

        await scope.SourceSettings.SaveAsync(
            RepositorySourceSettings.SourceName,
            "http://mcp.internal:8080",
            isEnabled: true,
            updatedById: scope.User.Id);

        var state = await Reading(scope).GetAsync();

        Assert.Equal("http://mcp.internal:8080", state.Endpoint);
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
            RepositorySourceSettings.SourceName,
            "http://chosen-in-the-ui:8080",
            isEnabled: true,
            updatedById: scope.User.Id);

        var configured = new KnowledgeSourceOptions
        {
            RepositoryProvider = KnowledgeSourceOptions.McpProvider,
            RepositoryEndpoint = "http://declared-in-config:9999",
        };

        var state = await Reading(scope, configured).GetAsync();

        Assert.Equal("http://chosen-in-the-ui:8080", state.Endpoint);
    }

    [Fact]
    public async Task Clearing_the_override_hands_control_back_to_configuration()
    {
        await using var scope = fixture.NewScope();

        await scope.SourceSettings.SaveAsync(
            RepositorySourceSettings.SourceName,
            "http://chosen-in-the-ui:8080",
            isEnabled: true,
            updatedById: scope.User.Id);

        await scope.SourceSettings.ClearAsync(RepositorySourceSettings.SourceName);

        var configured = new KnowledgeSourceOptions
        {
            RepositoryProvider = KnowledgeSourceOptions.McpProvider,
            RepositoryEndpoint = "http://declared-in-config:9999",
        };

        var state = await Reading(scope, configured).GetAsync();

        // Removing the override is deliberately different from saving an empty
        // one: the first restores the deployment's baseline, the second is an
        // administrator switching the source off.
        Assert.Equal("http://declared-in-config:9999", state.Endpoint);
        Assert.True(state.IsFromConfiguration);
    }

    [Fact]
    public async Task An_endpoint_left_in_configuration_does_not_re_enable_a_none_provider()
    {
        await using var scope = fixture.NewScope();
        await scope.SourceSettings.ClearAsync(RepositorySourceSettings.SourceName);

        // The realistic mistake: someone switches the provider back to "none"
        // and leaves the address behind. That must not count as configured.
        var configured = new KnowledgeSourceOptions
        {
            RepositoryProvider = KnowledgeSourceOptions.NoneProvider,
            RepositoryEndpoint = "http://left-behind:9999",
        };

        var state = await Reading(scope, configured).GetAsync();

        Assert.Null(state.Endpoint);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public async Task Switching_the_source_off_keeps_its_address()
    {
        await using var scope = fixture.NewScope();

        await scope.SourceSettings.SaveAsync(
            RepositorySourceSettings.SourceName,
            "http://mcp.internal:8080",
            isEnabled: false,
            updatedById: scope.User.Id);

        var state = await Reading(scope).GetAsync();

        // Turning a source off during an outage must not throw away the address
        // it will need back.
        Assert.Equal("http://mcp.internal:8080", state.Endpoint);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public async Task The_sources_screen_reports_what_was_saved()
    {
        await using var scope = fixture.NewScope();

        await scope.SourceSettings.SaveAsync(
            RepositorySourceSettings.SourceName,
            "http://mcp.internal:8080",
            isEnabled: true,
            updatedById: scope.User.Id);

        var sources = await scope.Knowledge.DescribeSourcesAsync();
        var repositories = Assert.Single(sources, source => source.Name == "repositories");

        // Still inactive — the client has not shipped — but the detail line has
        // to distinguish "nobody set an address" from "an address is set and
        // waiting on the client".
        Assert.Equal("inactive", repositories.State);
        Assert.Contains("mcp.internal:8080", repositories.Detail);
    }
}
