namespace DocHub.DataAccess.Entities;

/// <summary>
/// Which repository the hub mirrors, as an administrator set it.
///
/// A single row, overlaying the <c>GitLab</c> configuration section rather than
/// replacing it: an unset field here means "whatever the deployment configured",
/// so a box provisioned by environment variables keeps working untouched and a
/// row is only written when somebody changes something in the UI.
///
/// Which repository is mirrored decides what the whole installation contains,
/// which is why it lived in configuration to begin with. It is here now for the
/// same reason the MCP servers are: repointing the hub is operational, and it
/// should not need a text editor on the box and an app-pool recycle.
/// </summary>
public class RepositorySettings
{
    /// <summary>
    /// The hub mirrors one repository, so there is one row. Fixed rather than
    /// generated, and constrained in the database, because "the settings" is a
    /// single thing — a second row would be two answers to one question.
    /// </summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Instance root, e.g. <c>https://gitlab.example.org</c>. Empty falls back to configuration.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Namespaced project path as GitLab spells it, e.g. <c>team/docs</c>.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    /// <summary>
    /// Directory within the repository to mirror. Unlike the others, an empty
    /// value here is a real choice — "mirror the whole repository" — so it is
    /// stored alongside a flag rather than read as "not set".
    /// </summary>
    public string SubPath { get; set; } = string.Empty;

    /// <summary>
    /// True once the sub-path has been set through the UI, whatever it was set
    /// to. Without it, clearing the field to mirror the repository root would
    /// silently fall back to the configured sub-path instead.
    /// </summary>
    public bool HasSubPath { get; set; }

    /// <summary>
    /// The <c>read_repository</c> token, encrypted with Data Protection before
    /// it is written. Null means none has been set here and the configured one
    /// is used; it is never read back out to a client, only replaced or cleared.
    /// </summary>
    public string? ProtectedToken { get; set; }

    /// <summary>
    /// The webhook shared secret, encrypted the same way. Empty refuses every
    /// delivery, so "not set here" and "set here to nothing" are different
    /// things and null is the only one that falls back to configuration.
    /// </summary>
    public string? ProtectedWebhookSecret { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who last changed it. Null only if the row predates an actor being known.</summary>
    public Guid? UpdatedById { get; set; }
}
