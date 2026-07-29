using Microsoft.Extensions.Options;

namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Stands in for the source-code repository source until a real MCP server
/// exists (phase 7). Contributes nothing to every question.
///
/// Registering a source that returns nothing looks pointless, and is the point:
/// it keeps the fan-out, the merge and the sources screen exercised against
/// more than one source from the day the abstraction lands, so the real client
/// arrives into a shape that already works. A second source appearing for the
/// first time in phase 7 would be a second source debugged for the first time
/// in phase 7.
///
/// It reports itself <see cref="KnowledgeSourceState.Inactive"/>, never
/// <see cref="KnowledgeSourceState.Unavailable"/> — nothing is broken, and a
/// permanent red light on the sources screen is one users learn to ignore.
/// </summary>
internal sealed class NullRepositoryKnowledgeSource(IOptions<KnowledgeSourceOptions> options)
    : IKnowledgeSource
{
    private readonly KnowledgeSourceOptions options = options.Value;

    public string Name => "repositories";

    public string DisplayName => "Repositories";

    public string Description =>
        "Source code and READMEs from the team's repositories, reached over MCP.";

    public Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new KnowledgeSourceStatus(
            KnowledgeSourceState.Inactive,
            $"No MCP server is configured, so answers are grounded in documents only. Set "
            + $"KnowledgeSources:RepositoryProvider to '{KnowledgeSourceOptions.McpProvider}' "
            + "once one is available."));

    public Task<KnowledgeSearchResult> SearchAsync(
        KnowledgeQuery query,
        CancellationToken ct = default) =>
        Task.FromResult(KnowledgeSearchResult.Empty);
}
