namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Stands in for the source-code repository source until the real MCP client
/// exists. Contributes nothing to every question.
///
/// Registering a source that returns nothing looks pointless, and is the point:
/// it keeps the fan-out, the merge and the sources screen exercised against
/// more than one source from the day the abstraction lands, so the real client
/// arrives into a shape that already works. A second source appearing for the
/// first time in phase 7 would be a second source debugged for the first time
/// in phase 7.
///
/// It reports what an administrator has actually configured, so the sources
/// screen can tell "nobody has set an address" apart from "an address is set
/// and the client to use it has not shipped yet". Neither is
/// <see cref="KnowledgeSourceState.Unavailable"/> — nothing is broken, and a
/// permanent red light is one users learn to ignore.
/// </summary>
internal sealed class NullRepositoryKnowledgeSource(IRepositorySourceSettings settings)
    : IKnowledgeSource
{
    public string Name => "repositories";

    public string DisplayName => "Repositories";

    public string Description =>
        "Source code and READMEs from the team's repositories, reached over MCP.";

    public async Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default)
    {
        var state = await settings.GetAsync(ct);

        var detail = (state.Endpoint, state.IsEnabled) switch
        {
            (null, _) =>
                "No MCP server address is set, so answers are grounded in documents only. "
                + "An administrator can set one on this screen.",

            (_, false) =>
                $"Switched off by an administrator. The address ({state.Endpoint}) is kept, so "
                + "turning it back on needs no retyping.",

            _ =>
                $"Address set to {state.Endpoint}, but the MCP client itself has not shipped yet, "
                + "so this source still contributes nothing. Nothing else needs configuring — it "
                + "starts working when the client lands.",
        };

        return new KnowledgeSourceStatus(KnowledgeSourceState.Inactive, detail);
    }

    public Task<KnowledgeSearchResult> SearchAsync(
        KnowledgeQuery query,
        CancellationToken ct = default) =>
        Task.FromResult(KnowledgeSearchResult.Empty);
}
