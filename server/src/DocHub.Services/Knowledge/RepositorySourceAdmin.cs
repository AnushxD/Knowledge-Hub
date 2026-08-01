using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Knowledge;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Knowledge;

/// <summary>
/// Managing where each repository source points.
///
/// The rules live here rather than in the controller because they are
/// decisions, not plumbing: what counts as a usable address, what clearing
/// means as against disabling, and which sources exist to be edited at all.
/// </summary>
public interface IRepositorySourceAdmin
{
    /// <summary>Every declared source, in the order configuration lists them.</summary>
    Task<IReadOnlyList<RepositorySourceViewModel>> ListAsync(CancellationToken ct = default);

    Task<RepositorySourceViewModel> GetAsync(string name, CancellationToken ct = default);

    Task<RepositorySourceViewModel> SaveAsync(
        string name,
        UpdateRepositorySourceRequest request,
        CancellationToken ct = default);

    /// <summary>Removes the override so the deployment's configuration applies again.</summary>
    Task<RepositorySourceViewModel> ResetAsync(string name, CancellationToken ct = default);

    Task<RepositoryProbeViewModel> ProbeAsync(
        string name,
        string? endpoint,
        CancellationToken ct = default);
}

internal sealed class RepositorySourceAdmin(
    IRepositorySourceSettingRepository settings,
    IRepositorySourceSettings effective,
    IRepositoryEndpointProbe probe,
    ICurrentUser currentUser,
    ILogger<RepositorySourceAdmin> logger) : IRepositorySourceAdmin
{
    public async Task<IReadOnlyList<RepositorySourceViewModel>> ListAsync(
        CancellationToken ct = default)
    {
        var states = await effective.ListAsync(ct);
        var stored = await settings.ListAsync(ct);

        return
        [
            .. states.Select(state => ToViewModel(
                state,
                stored.FirstOrDefault(row => string.Equals(
                    row.Name, state.Name, StringComparison.OrdinalIgnoreCase))?.UpdatedAt)),
        ];
    }

    public async Task<RepositorySourceViewModel> GetAsync(
        string name,
        CancellationToken ct = default)
    {
        var state = await Require(name, ct);
        var stored = await settings.GetAsync(state.Name, ct);

        return ToViewModel(state, stored?.UpdatedAt);
    }

    public async Task<RepositorySourceViewModel> SaveAsync(
        string name,
        UpdateRepositorySourceRequest request,
        CancellationToken ct = default)
    {
        // Resolved first so the stored row is keyed by the name configuration
        // uses, not by whatever casing the caller typed into the route.
        var state = await Require(name, ct);
        var endpoint = Normalise(request.Endpoint);

        await settings.SaveAsync(state.Name, endpoint, request.IsEnabled, currentUser.Id, ct);

        logger.LogInformation(
            "User {UserId} set repository source {Source} to {Endpoint} (enabled: {IsEnabled})",
            currentUser.Id, state.Name, endpoint ?? "(none)", request.IsEnabled);

        return await GetAsync(state.Name, ct);
    }

    public async Task<RepositorySourceViewModel> ResetAsync(
        string name,
        CancellationToken ct = default)
    {
        var state = await Require(name, ct);

        await settings.ClearAsync(state.Name, ct);

        logger.LogInformation(
            "User {UserId} reset repository source {Source} to configuration",
            currentUser.Id, state.Name);

        return await GetAsync(state.Name, ct);
    }

    public async Task<RepositoryProbeViewModel> ProbeAsync(
        string name,
        string? endpoint,
        CancellationToken ct = default)
    {
        var state = await Require(name, ct);

        // Probing what is currently in effect is the useful default: it is how
        // an administrator checks a source that has stopped working without
        // retyping its address.
        var target = Normalise(endpoint) ?? state.Endpoint;

        if (target is null)
            throw new ValidationException("There is no address to test.");

        var result = await probe.ProbeAsync(target, ct);

        return new RepositoryProbeViewModel(result.IsReachable, result.Detail);
    }

    /// <summary>
    /// The source by that name, or 404. A name configuration does not declare
    /// has no override to edit — creating one would store a row nothing reads.
    /// </summary>
    private async Task<RepositorySourceState> Require(string name, CancellationToken ct) =>
        await effective.GetAsync(name, ct)
        ?? throw new NotFoundException("Repository source", name);

    private static RepositorySourceViewModel ToViewModel(
        RepositorySourceState state,
        DateTimeOffset? updatedAt) =>
        new(
            state.Name,
            state.DisplayName,
            state.Endpoint,
            state.IsEnabled,
            state.IsFromConfiguration,
            state.ConfiguredEndpoint,
            updatedAt);

    /// <summary>
    /// Blank becomes null — "no address", which switches the source off without
    /// pretending an empty string is one.
    ///
    /// Anything else must be an absolute http or https URL. The app fetches
    /// this address on the server's behalf, so a scheme like <c>file:</c> or
    /// <c>ftp:</c> would turn an admin text box into a way to read things the
    /// server can reach and the administrator cannot. Restricting the scheme
    /// does not make this risk-free — an administrator can still point it at any
    /// internal host — but that is a deliberate, role-gated capability rather
    /// than an accident.
    /// </summary>
    private static string? Normalise(string? endpoint)
    {
        var trimmed = endpoint?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new ValidationException("Enter a full address, including http:// or https://.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ValidationException("The address must start with http:// or https://.");

        return uri.ToString();
    }
}
