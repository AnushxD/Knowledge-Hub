using Microsoft.Extensions.Options;

namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Stands in for the repository sources on a deployment that has none.
/// Contributes nothing to every question.
///
/// Registering a source that returns nothing looks pointless, and is the point:
/// it keeps the fan-out, the merge and the sources screen exercised against
/// more than one source on a machine with no MCP server — which is every
/// development machine. Without it the second source would only ever run in
/// production, and would be debugged there.
///
/// Its state is <see cref="KnowledgeSourceState.Inactive"/>, never
/// <see cref="KnowledgeSourceState.Unavailable"/>: nothing is broken, and a
/// permanent red light is one users learn to ignore.
/// </summary>
internal sealed class NullRepositoryKnowledgeSource(IOptions<KnowledgeSourceOptions> options)
    : IKnowledgeSource
{
    private readonly KnowledgeSourceOptions options = options.Value;

    public string Name => "repositories";

    public string DisplayName => "Repositories";

    public string Description =>
        "Source code and READMEs from the team's repositories, reached over MCP.";

    public Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default)
    {
        // Declared servers with the provider left at "none" is the interesting
        // case: everything is filled in and nothing is being searched, which is
        // otherwise indistinguishable from a server that is quietly failing.
        var declared = options.Repositories.Count;

        var detail = declared == 0
            ? "No repository servers are configured, so answers are grounded in documents "
              + "only. Adding one is a configuration change: KnowledgeSources:Repositories."
            : $"{declared} repository server{(declared == 1 ? " is" : "s are")} configured, but "
              + "KnowledgeSources:RepositoryProvider is 'none', so none of them is searched. "
              + "Set it to 'mcp' to turn them on.";

        return Task.FromResult(new KnowledgeSourceStatus(KnowledgeSourceState.Inactive, detail));
    }

    public Task<KnowledgeSearchResult> SearchAsync(
        KnowledgeQuery query,
        CancellationToken ct = default) =>
        Task.FromResult(KnowledgeSearchResult.Empty);
}
