namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Stands in for the repository sources when there are none to search.
/// Contributes nothing to every question.
///
/// A source that returns nothing looks pointless, and is the point: it keeps
/// the fan-out, the merge and the sources screen exercised against more than
/// one source on a machine with no MCP server — which is every development
/// machine and every fresh install. Without it the second source would only
/// ever run where servers exist, and would be debugged there.
///
/// Its state is <see cref="KnowledgeSourceState.Inactive"/>, never
/// <see cref="KnowledgeSourceState.Unavailable"/>: nothing is broken, and a
/// permanent red light is one users learn to ignore. The reason is passed in,
/// because "nobody has added a server" and "this deployment has repository
/// search switched off" call for different things to be done about them.
/// </summary>
internal sealed class NullRepositoryKnowledgeSource(string detail) : IKnowledgeSource
{
    public string Name => "repositories";

    public string DisplayName => "Repositories";

    public string Description =>
        "Source code and READMEs from the team's repositories, reached over MCP.";

    public Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new KnowledgeSourceStatus(KnowledgeSourceState.Inactive, detail));

    public Task<KnowledgeSearchResult> SearchAsync(
        KnowledgeQuery query,
        CancellationToken ct = default) =>
        Task.FromResult(KnowledgeSearchResult.Empty);
}
