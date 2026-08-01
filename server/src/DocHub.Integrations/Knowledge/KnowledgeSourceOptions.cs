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

    /// <summary>No repository server wired up; the stub stands in and returns nothing.</summary>
    public const string NoneProvider = "none";

    /// <summary>Real MCP servers, one source per entry in <see cref="Repositories"/>.</summary>
    public const string McpProvider = "mcp";

    /// <summary>
    /// Chosen at startup rather than probed, matching the embedding and LLM
    /// providers: which sources an answer was grounded in has to be a property
    /// of the deployment, not something that varies question to question.
    /// </summary>
    public string RepositoryProvider { get; set; } = NoneProvider;

    /// <summary>
    /// The repository servers to search, one knowledge source each.
    ///
    /// A list rather than one address because a team's code is routinely spread
    /// over more than one index, and the fan-out already searches every source
    /// concurrently under its own deadline — so a second server costs an entry
    /// here and nothing else.
    ///
    /// Declaring them is a deployment decision: an administrator can move or
    /// switch one off from the UI, but adding one is a configuration change,
    /// for the same reason the provider is.
    /// </summary>
    public IList<RepositorySourceOptions> Repositories { get; set; } = [];

    /// <summary>
    /// Passages to ask each tool for. Sent as the tool's <c>maxResults</c>
    /// argument, and capped again on the way back, because a server is free to
    /// ignore it.
    /// </summary>
    public int RepositoryMaxResults { get; set; } = 8;
}

/// <summary>One repository server, as configuration declares it.</summary>
public sealed class RepositorySourceOptions
{
    /// <summary>
    /// Stable identifier, unique across the deployment. It keys the
    /// administrator's override row and appears in the API's routes, so
    /// renaming one abandons its override rather than moving it.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What to call it on screen, and in the sentence naming a source that
    /// could not be searched. Defaults to <see cref="Name"/>, which reads well
    /// for a chosen name and badly for a hostname.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// One line saying what this server indexes. Two servers exposing identical
    /// tools are told apart by this and nothing else, so it is worth writing.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The MCP server's address, and the baseline for it: an administrator's
    /// override wins, and clearing that override restores this.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Which of the server's tools to search with. Empty means "discover it":
    /// the first tool whose name contains "search" is used.
    ///
    /// Discovery is a convenience for getting started, not the intended
    /// long-term setting. An MCP server is free to expose several tools, and
    /// picking one by substring is a guess — naming it here is how a deployment
    /// stops guessing.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>The name to show, falling back to the identifier.</summary>
    public string ResolvedDisplayName =>
        string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName.Trim();

    /// <summary>The description to show, falling back to a generic one.</summary>
    public string ResolvedDescription =>
        string.IsNullOrWhiteSpace(Description)
            ? "Source code and READMEs from the team's repositories, reached over MCP."
            : Description.Trim();
}
