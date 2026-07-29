using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Knowledge;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Knowledge;

/// <summary>
/// Reconciles the administrator's override with what configuration declares.
///
/// The rule is: <b>an override wins if there is one, otherwise configuration
/// applies.</b> Configuration therefore remains the deployment's baseline — a
/// deployment that always wants a repository source can still say so and be
/// certain of it — while day-to-day changes are a UI edit rather than an
/// app-pool variable and a recycle.
///
/// Lives in Services because only this layer can see both the repository and
/// the options; the contract it satisfies is defined in Integrations, where the
/// MCP client that consumes it lives.
/// </summary>
internal sealed class RepositorySourceSettings(
    IRepositorySourceSettingRepository settings,
    IOptions<KnowledgeSourceOptions> options) : IRepositorySourceSettings
{
    /// <summary>The single source this applies to. Matches the source's own Name.</summary>
    public const string SourceName = "repositories";

    private readonly KnowledgeSourceOptions options = options.Value;

    public async Task<RepositorySourceState> GetAsync(CancellationToken ct = default)
    {
        var stored = await settings.GetAsync(SourceName, ct);

        if (stored is not null)
        {
            return new RepositorySourceState(
                stored.Endpoint,
                stored.IsEnabled,
                IsFromConfiguration: false);
        }

        // No override. Configuration only counts as an address when the
        // provider is actually set to one — leaving an endpoint behind after
        // switching the provider back to "none" must not quietly re-enable it.
        var configured = options.RepositoryProvider == KnowledgeSourceOptions.McpProvider
            && !string.IsNullOrWhiteSpace(options.RepositoryEndpoint);

        return new RepositorySourceState(
            configured ? options.RepositoryEndpoint!.Trim() : null,
            IsEnabled: configured,
            IsFromConfiguration: true);
    }
}
