using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Knowledge;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Knowledge;

/// <summary>
/// Managing the repository source's address.
///
/// The rules live here rather than in the controller because they are
/// decisions, not plumbing: what counts as a usable address, what clearing
/// means as against disabling, and who is allowed to be told the difference.
/// </summary>
public interface IRepositorySourceAdmin
{
    Task<RepositorySourceViewModel> GetAsync(CancellationToken ct = default);

    Task<RepositorySourceViewModel> SaveAsync(
        UpdateRepositorySourceRequest request,
        CancellationToken ct = default);

    /// <summary>Removes the override so the deployment's configuration applies again.</summary>
    Task<RepositorySourceViewModel> ResetAsync(CancellationToken ct = default);

    Task<RepositoryProbeViewModel> ProbeAsync(string? endpoint, CancellationToken ct = default);
}

internal sealed class RepositorySourceAdmin(
    IRepositorySourceSettingRepository settings,
    IRepositorySourceSettings effective,
    IRepositoryEndpointProbe probe,
    ICurrentUser currentUser,
    ILogger<RepositorySourceAdmin> logger) : IRepositorySourceAdmin
{
    public async Task<RepositorySourceViewModel> GetAsync(CancellationToken ct = default)
    {
        var state = await effective.GetAsync(ct);
        var stored = await settings.GetAsync(RepositorySourceSettings.SourceName, ct);

        return new RepositorySourceViewModel(
            state.Endpoint,
            state.IsEnabled,
            state.IsFromConfiguration,
            stored?.UpdatedAt);
    }

    public async Task<RepositorySourceViewModel> SaveAsync(
        UpdateRepositorySourceRequest request,
        CancellationToken ct = default)
    {
        var endpoint = Normalise(request.Endpoint);

        await settings.SaveAsync(
            RepositorySourceSettings.SourceName,
            endpoint,
            request.IsEnabled,
            currentUser.Id,
            ct);

        logger.LogInformation(
            "User {UserId} set the repository source to {Endpoint} (enabled: {IsEnabled})",
            currentUser.Id, endpoint ?? "(none)", request.IsEnabled);

        return await GetAsync(ct);
    }

    public async Task<RepositorySourceViewModel> ResetAsync(CancellationToken ct = default)
    {
        await settings.ClearAsync(RepositorySourceSettings.SourceName, ct);

        logger.LogInformation(
            "User {UserId} reset the repository source to configuration", currentUser.Id);

        return await GetAsync(ct);
    }

    public async Task<RepositoryProbeViewModel> ProbeAsync(
        string? endpoint,
        CancellationToken ct = default)
    {
        // Probing what is currently in effect is the useful default: it is how
        // an administrator checks a source that has stopped working without
        // retyping its address.
        var target = Normalise(endpoint) ?? (await effective.GetAsync(ct)).Endpoint;

        if (target is null)
            throw new ValidationException("There is no address to test.");

        var result = await probe.ProbeAsync(target, ct);

        return new RepositoryProbeViewModel(result.IsReachable, result.Detail);
    }

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
