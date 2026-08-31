using DocHub.DataAccess.Repositories;
using DocHub.Integrations.SourceControl;
using DocHub.Services.Repository;
using DocHub.Services.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Tests;

/// <summary>
/// Pointing the hub at a repository from the UI.
///
/// The rules worth holding here are about honesty and blast radius: what is in
/// force is what the next sync reads, a secret goes in and never comes back
/// out, and a repository nobody has chosen says so rather than looking broken.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class RepositorySettingsTests(StackFixture fixture)
{
    /// <summary>
    /// The row is global to the database, so a test that leaves one behind
    /// would decide where the next test's hub is pointed.
    /// </summary>
    private static async Task ClearAsync(StackFixture.Scope scope) =>
        await scope.Db.RepositorySettings.ExecuteDeleteAsync();

    [Fact]
    public async Task The_configured_repository_is_in_force_until_one_is_saved()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        var settings = await scope.RepositorySettings.GetAsync();

        Assert.False(settings.IsSaved);
        Assert.True(settings.IsConfigured);
        Assert.Equal("https://gitlab.test", settings.BaseUrl);
        Assert.Equal(scope.Repository.ProjectPath, settings.ProjectPath);
    }

    [Fact]
    public async Task A_saved_repository_is_in_force_without_a_restart()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        try
        {
            await scope.RepositorySettings.SaveAsync(new UpdateRepositorySettingsRequest
            {
                BaseUrl = "https://gitlab.example.org/",
                ProjectPath = "/team/handbook/",
                Branch = "trunk",
                SubPath = "docs",
            });

            // The next sync reads this, not the configuration it was built
            // with — which is the whole point of the setting being editable.
            var inForce = await scope.SettingsInForce.GetAsync();

            Assert.Equal("https://gitlab.example.org", inForce.BaseUrl);
            Assert.Equal("team/handbook", inForce.ProjectPath);
            Assert.Equal("trunk", inForce.Branch);
            Assert.Equal("docs", inForce.SubPath);
            Assert.Equal(RepositoryConfigurationOrigin.Saved, inForce.Origin);

            // And the library screen names the repository it would now mirror.
            var status = await scope.Mirror.GetStatusAsync();
            Assert.Equal("team/handbook", status.ProjectPath);
            Assert.True(status.IsConfigured);
        }
        finally
        {
            await ClearAsync(scope);
        }
    }

    [Fact]
    public async Task A_token_is_encrypted_before_it_is_stored_and_never_returned()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        try
        {
            var saved = await scope.RepositorySettings.SaveAsync(Draft(token: "glpat-secret"));

            Assert.True(saved.HasToken);

            var row = await new RepositorySettingsRepository(scope.Db).GetAsync();

            // What is in the column is what the protector produced, not the
            // token as typed — in production that is Data Protection's
            // ciphertext, and here the fake's marker stands in for it.
            Assert.NotNull(row!.ProtectedToken);
            Assert.NotEqual("glpat-secret", row.ProtectedToken);
            Assert.Equal("glpat-secret", scope.Protector.Unprotect(row.ProtectedToken));

            // The client is told a token is held, and nothing more. Every
            // property of the view model is checked, so adding one that leaks
            // the token later fails here.
            Assert.DoesNotContain(
                "glpat-secret",
                string.Join(
                    '|',
                    saved.BaseUrl, saved.ProjectPath, saved.Branch, saved.SubPath));

            // And it is the token the GitLab client will actually present.
            Assert.Equal("glpat-secret", (await scope.SettingsInForce.GetAsync()).Token);
        }
        finally
        {
            await ClearAsync(scope);
        }
    }

    [Fact]
    public async Task Saving_without_a_token_leaves_the_stored_one_alone()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        try
        {
            await scope.RepositorySettings.SaveAsync(Draft(token: "glpat-secret"));

            // Changing the branch must not cost the credential: the screen
            // never had the token to send back.
            var saved = await scope.RepositorySettings.SaveAsync(
                Draft(token: null) with { Branch = "release" });

            Assert.True(saved.HasToken);
            Assert.Equal("glpat-secret", (await scope.SettingsInForce.GetAsync()).Token);
        }
        finally
        {
            await ClearAsync(scope);
        }
    }

    [Fact]
    public async Task An_empty_token_clears_it()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        try
        {
            await scope.RepositorySettings.SaveAsync(Draft(token: "glpat-secret"));

            var saved = await scope.RepositorySettings.SaveAsync(Draft(token: string.Empty));

            // Cleared here means "fall back to the deployment's", which in this
            // fixture is nothing at all — a public project, read anonymously.
            Assert.False(saved.HasToken);
            Assert.Equal(string.Empty, (await scope.SettingsInForce.GetAsync()).Token);
        }
        finally
        {
            await ClearAsync(scope);
        }
    }

    [Fact]
    public async Task A_token_that_cannot_be_decrypted_is_reported_rather_than_shown_as_unset()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        try
        {
            await scope.RepositorySettings.SaveAsync(Draft(token: "glpat-secret"));

            // The Data Protection key ring did not survive a recycle, which is
            // exactly what an unset Authentication:KeyPath causes on IIS. Read
            // through a freshly built reader for the same reason: what is being
            // tested is the state the process comes back up in.
            scope.Protector.KeysLost = true;

            var settings = await AdminAfterRestart(scope).GetAsync();

            // "Not set" would send an administrator looking for who removed it;
            // the truth is that it is there and unreadable, and the fix is to
            // set it again.
            Assert.True(settings.TokenIsUnreadable);
            Assert.False(settings.HasToken);
        }
        finally
        {
            scope.Protector.KeysLost = false;
            await ClearAsync(scope);
        }
    }

    [Fact]
    public async Task A_webhook_secret_saved_in_the_UI_is_the_one_a_delivery_is_checked_against()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        try
        {
            await scope.RepositorySettings.SaveAsync(Draft(webhookSecret: "rotated-secret"));

            Assert.Equal(
                WebhookOutcome.Queued,
                await scope.Webhook.HandleAsync("rotated-secret", "Push Hook", "refs/heads/main"));

            // The configured one is superseded, not merely joined: rotating a
            // secret has to actually retire the old one.
            Assert.Equal(
                WebhookOutcome.Rejected,
                await scope.Webhook.HandleAsync("test-secret", "Push Hook", "refs/heads/main"));
        }
        finally
        {
            await ClearAsync(scope);
        }
    }

    [Fact]
    public async Task A_cleared_sub_path_mirrors_the_whole_repository()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        // A deployment that configured a sub-path, so that "empty" has
        // something to fall back to and can be shown not to.
        var settings = new StoredRepositorySettings(
            new SingleServiceScopeFactory(new RepositorySettingsRepository(scope.Db)),
            scope.Protector,
            Options.Create(new GitLabOptions
            {
                BaseUrl = "https://gitlab.test",
                ProjectPath = scope.Repository.ProjectPath,
                Branch = "main",
                SubPath = "doc/development",
            }),
            NullLogger<StoredRepositorySettings>.Instance);

        Assert.Equal("doc/development", (await settings.GetAsync()).SubPath);

        try
        {
            await scope.RepositorySettings.SaveAsync(Draft() with { SubPath = string.Empty });

            // Empty is a choice — mirror everything — and reading it as "unset"
            // would quietly put the configured sub-path back.
            Assert.Equal(string.Empty, (await settings.RefreshAsync()).SubPath);
        }
        finally
        {
            await ClearAsync(scope);
        }
    }

    [Fact]
    public async Task A_project_path_pasted_as_a_whole_URL_is_refused()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        // GitLab answers 404 for this with no hint as to why, so it is caught
        // here where the message can say what the field actually wants.
        var failure = await Assert.ThrowsAsync<ValidationException>(
            () => scope.RepositorySettings.SaveAsync(
                Draft() with { ProjectPath = "https://gitlab.example.org/team/docs" }));

        Assert.Contains("'team/docs'", failure.Message);
    }

    [Fact]
    public async Task An_address_is_tested_with_the_token_already_held()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        try
        {
            await scope.RepositorySettings.SaveAsync(Draft(token: "glpat-secret"));

            await scope.RepositorySettings.TestAsync(Draft(token: null) with { Branch = "trunk" });

            // Checking a changed branch must not require pasting the
            // credential back in — the screen never had it.
            Assert.Equal("glpat-secret", scope.ConnectionProbe.LastCandidate!.Token);
            Assert.Equal("trunk", scope.ConnectionProbe.LastCandidate.Branch);
        }
        finally
        {
            await ClearAsync(scope);
        }
    }

    [Fact]
    public async Task A_hub_pointed_nowhere_says_so_instead_of_looking_empty()
    {
        await using var scope = fixture.NewScope();
        await ClearAsync(scope);

        var nothingConfigured = new StoredRepositorySettings(
            new SingleServiceScopeFactory(new RepositorySettingsRepository(scope.Db)),
            scope.Protector,
            Options.Create(new GitLabOptions { Branch = "main" }),
            NullLogger<StoredRepositorySettings>.Instance);

        var mirror = new RepositoryMirrorService(
            scope.Repository,
            new DocumentRepository(scope.Db),
            new FolderRepository(scope.Db),
            new RepositorySyncStateRepository(scope.Db),
            scope.Ingestion,
            new StackFixture.RecordingIngestionQueue(),
            scope.Activity,
            nothingConfigured,
            NullLogger<RepositoryMirrorService>.Instance);

        var status = await mirror.GetStatusAsync();

        Assert.False(status.IsConfigured);
        Assert.Equal("never", status.Outcome);

        // And asking for a sync writes no failed run: nothing is broken, the
        // hub has simply not been pointed anywhere yet.
        var afterSync = await mirror.SyncAsync(actorId: null);

        Assert.False(afterSync.IsConfigured);
        Assert.Equal("never", afterSync.Outcome);
    }

    /// <summary>
    /// The administration service as it would be after a restart: a settings
    /// reader with nothing cached, over the row already in the database.
    /// </summary>
    private static IRepositorySettingsAdmin AdminAfterRestart(StackFixture.Scope scope)
    {
        var repository = new RepositorySettingsRepository(scope.Db);

        var settings = new StoredRepositorySettings(
            new SingleServiceScopeFactory(repository),
            scope.Protector,
            Options.Create(new GitLabOptions
            {
                BaseUrl = "https://gitlab.test",
                ProjectPath = scope.Repository.ProjectPath,
                Branch = "main",
            }),
            NullLogger<StoredRepositorySettings>.Instance);

        return new RepositorySettingsAdmin(
            repository, settings, scope.ConnectionProbe, scope.Protector, scope.Activity,
            new StackFixture.TestCurrentUser(), NullLogger<RepositorySettingsAdmin>.Instance);
    }

    /// <summary>
    /// A valid change, so each test varies only the field it is about.
    /// </summary>
    private static UpdateRepositorySettingsRequest Draft(
        string? token = null,
        string? webhookSecret = null) =>
        new()
        {
            BaseUrl = "https://gitlab.example.org",
            ProjectPath = "team/handbook",
            Branch = "main",
            SubPath = "docs",
            Token = token,
            WebhookSecret = webhookSecret,
        };
}
