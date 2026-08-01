namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Which knowledge sources this deployment has, bound from the
/// "KnowledgeSources" section.
///
/// The document source is not configurable — it is the hub's own content, and a
/// documentation hub whose documents can be switched off is a different
/// product. Everything here is about the sources that come from elsewhere.
/// </summary>
public sealed class KnowledgeSourceOptions
{
    public const string SectionName = "KnowledgeSources";

    /// <summary>Repository search off for this deployment; the stub stands in.</summary>
    public const string NoneProvider = "none";

    /// <summary>
    /// Search the MCP servers an administrator has added on the sources screen.
    /// </summary>
    public const string McpProvider = "mcp";

    /// <summary>
    /// Whether this deployment searches repositories at all.
    ///
    /// <b>Which</b> servers exist is data, managed in the UI — a team's code
    /// moves, and that should not need a text editor on the box. Whether to
    /// search any of them is still a deployment decision, and one worth being
    /// able to make without deleting anybody's rows: it is the switch to reach
    /// for when every server is down or a network is being rebuilt.
    ///
    /// Defaults to on, matching appsettings.json. Off would be the safer-looking
    /// choice and is the wrong one: the servers are already off by default,
    /// because a fresh install has no rows, so this defaulting to "none" as well
    /// would only mean a server added in the UI silently does nothing until
    /// somebody edits a file — which is what the UI exists to avoid.
    /// </summary>
    public string RepositoryProvider { get; set; } = McpProvider;

    /// <summary>
    /// Passages to ask each tool for. Sent as the tool's <c>maxResults</c>
    /// argument, and capped again on the way back, because a server is free to
    /// ignore it.
    /// </summary>
    public int RepositoryMaxResults { get; set; } = 8;
}
