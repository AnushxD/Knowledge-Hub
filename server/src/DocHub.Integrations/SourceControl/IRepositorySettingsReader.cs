using Microsoft.Extensions.Options;

namespace DocHub.Integrations.SourceControl;

/// <summary>
/// The settings a call to GitLab actually uses, resolved rather than read
/// straight from configuration.
///
/// A snapshot record instead of <see cref="GitLabOptions"/> so that nothing
/// downstream can mutate what it was handed, and so "where did this value come
/// from" has one answer per call rather than one per field.
/// </summary>
/// <param name="Origin">
/// Whether these values came from the deployment's configuration or from what
/// an administrator saved. Carried because the screen has to say which, and
/// "the box is configured for gitlab.example.org" reads very differently from
/// "somebody pointed the hub at gitlab.example.org last Tuesday".
/// </param>
public sealed record RepositoryConfiguration(
    string BaseUrl,
    string ProjectPath,
    string Branch,
    string SubPath,
    string Token,
    string WebhookSecret,
    int TimeoutSeconds,
    long MaxFileBytes,
    RepositoryConfigurationOrigin Origin = RepositoryConfigurationOrigin.Configuration)
{
    /// <summary>
    /// Whether the hub knows which repository to mirror at all.
    ///
    /// An address and a project are the whole of it: everything else has a
    /// working default. False is a first-run state, not a fault — it is what a
    /// freshly installed hub looks like before an administrator points it
    /// somewhere, and every screen and health check says so in those words.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ProjectPath);

    public static RepositoryConfiguration From(GitLabOptions options) =>
        new(
            options.BaseUrl.Trim(),
            options.ProjectPath.Trim(),
            options.Branch.Trim(),
            options.SubPath.Trim(),
            options.Token,
            options.WebhookSecret,
            options.TimeoutSeconds,
            options.MaxFileBytes);
}

/// <summary>Where the settings in force came from.</summary>
public enum RepositoryConfigurationOrigin
{
    /// <summary>The <c>GitLab</c> section — appsettings, environment or Key Vault.</summary>
    Configuration,

    /// <summary>Saved in the UI by an administrator, overlaying the above.</summary>
    Saved,
}

/// <summary>
/// Supplies the repository settings in force.
///
/// Defined here rather than in Services for the usual reason: Integrations
/// references nothing, so a contract its clients depend on cannot live in a
/// layer above them. The default implementation reads configuration and
/// nothing else; Services replaces it with one that overlays what an
/// administrator saved, because that needs the database.
/// </summary>
public interface IRepositorySettingsReader
{
    /// <summary>
    /// The best-known settings, without touching anything. For callers that
    /// cannot await — a property naming the project on screen, the branch a
    /// webhook is compared against.
    /// </summary>
    RepositoryConfiguration Current { get; }

    /// <summary>
    /// The settings, re-read if the cached copy has gone stale. Every call that
    /// actually reaches GitLab goes through this, so a repository saved a
    /// second ago is the one that gets mirrored.
    /// </summary>
    ValueTask<RepositoryConfiguration> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// Configuration and nothing else. What a deployment gets before anybody
/// changes the repository in the UI, and what the Integrations layer uses on
/// its own — in tests, and anywhere the database is not in the picture.
/// </summary>
internal sealed class ConfiguredRepositorySettings(IOptions<GitLabOptions> options)
    : IRepositorySettingsReader
{
    public RepositoryConfiguration Current { get; } = RepositoryConfiguration.From(options.Value);

    public ValueTask<RepositoryConfiguration> GetAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(Current);
}
