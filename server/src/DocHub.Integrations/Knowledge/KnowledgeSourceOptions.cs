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

    /// <summary>A real MCP server. Phase 7.</summary>
    public const string McpProvider = "mcp";

    /// <summary>
    /// Chosen at startup rather than probed, matching the embedding and LLM
    /// providers: which sources an answer was grounded in has to be a property
    /// of the deployment, not something that varies question to question.
    /// </summary>
    public string RepositoryProvider { get; set; } = NoneProvider;

    /// <summary>The MCP server's address. Unused while the provider is "none".</summary>
    public string? RepositoryEndpoint { get; set; }
}
