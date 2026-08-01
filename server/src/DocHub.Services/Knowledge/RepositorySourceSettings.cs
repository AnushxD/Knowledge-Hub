using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Knowledge;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Knowledge;

/// <summary>
/// Reconciles each administrator override with what configuration declares.
///
/// The rule is: <b>an override wins if there is one, otherwise configuration
/// applies.</b> Configuration therefore remains the deployment's baseline — a
/// deployment that always wants a given repository source can still say so and
/// be certain of it — while day-to-day changes are a UI edit rather than an
/// app-pool variable and a recycle.
///
/// Configuration also decides which sources <i>exist</i>. An override row for a
/// name nothing declares is ignored rather than resurrected: that is what a
/// renamed or retired server leaves behind, and honouring it would put a source
/// on the screen that no deployment asked for.
///
/// Lives in Services because only this layer can see both the repository and
/// the options; the contract it satisfies is defined in Integrations, where the
/// MCP client that consumes it lives.
/// </summary>
internal sealed class RepositorySourceSettings(
    IRepositorySourceSettingRepository settings,
    IOptions<KnowledgeSourceOptions> options) : IRepositorySourceSettings
{
    private readonly KnowledgeSourceOptions options = options.Value;

    public async Task<RepositorySourceState?> GetAsync(
        string name,
        CancellationToken ct = default)
    {
        var declared = Declared(name);
        if (declared is null) return null;

        return Reconcile(declared, await settings.GetAsync(declared.Name, ct));
    }

    public async Task<IReadOnlyList<RepositorySourceState>> ListAsync(
        CancellationToken ct = default)
    {
        // One read for every source rather than one per source: there are only
        // ever a handful, and a loop of queries would grow with them.
        var stored = await settings.ListAsync(ct);

        return
        [
            .. options.Repositories.Select(declared => Reconcile(
                declared,
                stored.FirstOrDefault(row => string.Equals(
                    row.Name, declared.Name, StringComparison.OrdinalIgnoreCase)))),
        ];
    }

    /// <summary>The configuration entry for a name, matched case-insensitively.</summary>
    private RepositorySourceOptions? Declared(string name) =>
        options.Repositories.FirstOrDefault(source =>
            string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase));

    private RepositorySourceState Reconcile(
        RepositorySourceOptions declared,
        RepositorySourceSetting? stored)
    {
        if (stored is not null)
        {
            return new RepositorySourceState(
                declared.Name,
                declared.ResolvedDisplayName,
                stored.Endpoint,
                stored.IsEnabled,
                IsFromConfiguration: false);
        }

        // No override. Configuration only counts as an address when the
        // provider is actually set to one — leaving addresses behind after
        // switching the provider back to "none" must not quietly re-enable them.
        var configured = options.RepositoryProvider == KnowledgeSourceOptions.McpProvider
            && !string.IsNullOrWhiteSpace(declared.Endpoint);

        return new RepositorySourceState(
            declared.Name,
            declared.ResolvedDisplayName,
            configured ? declared.Endpoint!.Trim() : null,
            IsEnabled: configured,
            IsFromConfiguration: true);
    }
}
