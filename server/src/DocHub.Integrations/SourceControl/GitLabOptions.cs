namespace DocHub.Integrations.SourceControl;

/// <summary>
/// Strongly-typed configuration for the repository the hub mirrors, bound from
/// the "GitLab" section. One Options class per external dependency.
///
/// Unlike the MCP repository servers — which are rows an administrator adds in
/// the UI, because which code a team searches changes as the team's code moves
/// — this is a single deployment-level setting. The hub mirrors one repository,
/// and which one it is defines what the whole installation contains.
/// </summary>
public sealed class GitLabOptions
{
    public const string SectionName = "GitLab";

    /// <summary>Instance root, e.g. <c>https://gitlab.example.org</c>. No trailing path.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Namespaced project path as GitLab spells it, e.g. <c>team/docs</c>.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    public string Branch { get; set; } = "main";

    /// <summary>
    /// Directory within the repository to mirror, or empty for the whole thing.
    /// A repository is usually mostly code, so pointing this at <c>docs</c>
    /// keeps the tree on screen recognisable as documentation.
    /// </summary>
    public string SubPath { get; set; } = string.Empty;

    /// <summary>
    /// Personal or project access token with <c>read_repository</c>. The one
    /// real secret here — it belongs in user-secrets or Key Vault, never in a
    /// committed appsettings file.
    ///
    /// May be empty for a public project, which is what makes trying the hub
    /// against an open repository possible without provisioning anything.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret GitLab sends back as <c>X-Gitlab-Token</c> on a push hook.
    /// Empty refuses every webhook: the endpoint is anonymous by necessity, so
    /// with no secret configured there is nothing distinguishing GitLab from
    /// anyone else who can reach the box.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Per-request deadline. Generous compared with the knowledge-source
    /// timeout because nobody is waiting on a token: listing a large tree is a
    /// background job, not part of answering a question.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Files larger than this are mirrored as metadata but never fetched or
    /// indexed. A repository can hold a 400 MB binary, and streaming one
    /// through the extractor would take the ingestion worker down with it.
    /// </summary>
    public long MaxFileBytes { get; set; } = 25 * 1024 * 1024;
}
