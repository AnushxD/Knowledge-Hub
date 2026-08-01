using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Knowledge;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Knowledge;

/// <summary>
/// Every source the assistant may search, resolved fresh for each question.
///
/// Resolved rather than injected because repository servers are now data: an
/// administrator adds one on the sources screen and the next question searches
/// it, with no restart. A fixed <c>IEnumerable&lt;IKnowledgeSource&gt;</c> from
/// the container could only ever reflect the servers that existed at startup.
///
/// The document source is still injected — it is the hub's own content and
/// cannot be added or removed — so this is a fixed core plus a variable tail,
/// not a free-for-all.
/// </summary>
public interface IKnowledgeSourceCatalog
{
    Task<IReadOnlyList<IKnowledgeSource>> ResolveAsync(CancellationToken ct = default);
}

internal sealed class KnowledgeSourceCatalog(
    IEnumerable<IKnowledgeSource> fixedSources,
    IRepositorySourceSettingRepository repositories,
    IRepositoryKnowledgeSourceFactory factory,
    IOptions<KnowledgeSourceOptions> options) : IKnowledgeSourceCatalog
{
    private readonly KnowledgeSourceOptions options = options.Value;

    public async Task<IReadOnlyList<IKnowledgeSource>> ResolveAsync(CancellationToken ct = default)
    {
        // The deployment's switch, not an administrator's: with repository
        // search off, the added servers stay in the table untouched and the
        // placeholder explains why none of them is being searched.
        if (options.RepositoryProvider != KnowledgeSourceOptions.McpProvider)
        {
            return
            [
                .. fixedSources,
                factory.CreatePlaceholder(
                    "Repository search is switched off for this deployment. Set "
                    + "KnowledgeSources:RepositoryProvider to 'mcp' to turn it back on; any "
                    + "servers already added are kept."),
            ];
        }

        var added = await repositories.ListAsync(ct);

        // No servers yet keeps the placeholder, so the fan-out, the merge and
        // the sources screen are exercised against more than one source on a
        // machine that has none — which is every fresh install. Once real
        // servers exist it is dropped: it stands in for them, and leaving it
        // alongside would put a permanently inactive row next to the ones that
        // matter.
        if (added.Count == 0)
        {
            return
            [
                .. fixedSources,
                factory.CreatePlaceholder(
                    "No repository servers have been added, so answers are grounded in documents "
                    + "only. An administrator can add one on this screen."),
            ];
        }

        return
        [
            .. fixedSources,
            .. added.Select(row => factory.Create(new RepositorySourceDescriptor(
                row.Name,
                row.DisplayName,
                row.Endpoint,
                row.ToolName,
                row.IsEnabled))),
        ];
    }
}
