using System.Text.RegularExpressions;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Knowledge;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Knowledge;

/// <summary>
/// Adding, editing and removing the MCP repository servers the assistant
/// searches.
///
/// The rules live here rather than in the controller because they are
/// decisions, not plumbing: what counts as a usable address, what a name may
/// be, and that switching a server off is not the same as deleting it.
/// </summary>
public interface IRepositorySourceAdmin
{
    Task<IReadOnlyList<RepositorySourceViewModel>> ListAsync(CancellationToken ct = default);

    Task<RepositorySourceViewModel> GetAsync(string name, CancellationToken ct = default);

    Task<RepositorySourceViewModel> CreateAsync(
        CreateRepositorySourceRequest request,
        CancellationToken ct = default);

    Task<RepositorySourceViewModel> UpdateAsync(
        string name,
        UpdateRepositorySourceRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a server entirely. Distinct from switching it off, which keeps
    /// its address for when the outage ends.
    /// </summary>
    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Checks an address answers. Takes the address rather than a server's
    /// name, because the useful moment to test one is *before* it is added.
    /// </summary>
    Task<RepositoryProbeViewModel> ProbeAsync(string? endpoint, CancellationToken ct = default);
}

internal sealed partial class RepositorySourceAdmin(
    IRepositorySourceSettingRepository repositories,
    IRepositoryEndpointProbe probe,
    ICurrentUser currentUser,
    ILogger<RepositorySourceAdmin> logger) : IRepositorySourceAdmin
{
    public async Task<IReadOnlyList<RepositorySourceViewModel>> ListAsync(
        CancellationToken ct = default) =>
        [.. (await repositories.ListAsync(ct)).Select(ToViewModel)];

    public async Task<RepositorySourceViewModel> GetAsync(
        string name,
        CancellationToken ct = default) =>
        ToViewModel(await Require(name, ct));

    public async Task<RepositorySourceViewModel> CreateAsync(
        CreateRepositorySourceRequest request,
        CancellationToken ct = default)
    {
        var name = NormaliseName(request.Name);

        var created = await repositories.CreateAsync(
            new RepositorySourceSetting
            {
                Name = name,
                DisplayName = NormaliseDisplayName(request.DisplayName, name),
                Endpoint = RequireEndpoint(request.Endpoint),
                ToolName = request.ToolName?.Trim() ?? string.Empty,
                IsEnabled = request.IsEnabled,
                UpdatedById = currentUser.Id,
            },
            ct);

        if (created is null)
        {
            // The name is the citation's attribution and the route's key, so a
            // clash is a real conflict rather than something to resolve by
            // appending a number on the user's behalf.
            throw new ValidationException(
                $"A repository server named '{name}' already exists. Pick another name, or edit "
                + "the existing one.");
        }

        logger.LogInformation(
            "User {UserId} added repository server {Source} at {Endpoint}",
            currentUser.Id, created.Name, created.Endpoint);

        return ToViewModel(created);
    }

    public async Task<RepositorySourceViewModel> UpdateAsync(
        string name,
        UpdateRepositorySourceRequest request,
        CancellationToken ct = default)
    {
        var existing = await Require(name, ct);

        var updated = await repositories.UpdateAsync(
            existing.Name,
            NormaliseDisplayName(request.DisplayName, existing.Name),
            RequireEndpoint(request.Endpoint),
            request.ToolName?.Trim() ?? string.Empty,
            request.IsEnabled,
            currentUser.Id,
            ct) ?? throw new NotFoundException("Repository server", name);

        logger.LogInformation(
            "User {UserId} changed repository server {Source} to {Endpoint} (enabled: {IsEnabled})",
            currentUser.Id, updated.Name, updated.Endpoint, updated.IsEnabled);

        return ToViewModel(updated);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var existing = await Require(name, ct);

        await repositories.DeleteAsync(existing.Name, ct);

        // Answers already given keep citing it: their citations denormalise the
        // source's name, exactly as they denormalise a document's title, so
        // removing a server cannot rewrite what an old answer was grounded in.
        logger.LogInformation(
            "User {UserId} removed repository server {Source}", currentUser.Id, existing.Name);
    }

    public async Task<RepositoryProbeViewModel> ProbeAsync(
        string? endpoint,
        CancellationToken ct = default)
    {
        var target = RequireEndpoint(endpoint);
        var result = await probe.ProbeAsync(target, ct);

        return new RepositoryProbeViewModel(
            result.IsReachable,
            result.SpeaksMcp,
            result.Detail,
            result.Tools,
            result.SuggestedToolName,
            result.Repositories);
    }

    private async Task<RepositorySourceSetting> Require(string name, CancellationToken ct) =>
        await repositories.GetAsync(name, ct)
        ?? throw new NotFoundException("Repository server", name);

    private static RepositorySourceViewModel ToViewModel(RepositorySourceSetting source) =>
        new(
            source.Name,
            source.DisplayName,
            source.Endpoint,
            source.ToolName,
            source.IsEnabled,
            source.UpdatedAt);

    /// <summary>
    /// Lower-case letters, digits and hyphens. The name goes in a URL path and
    /// is recorded on every citation the server produces, so it is kept to
    /// characters that need no escaping in either place and read the same in
    /// both.
    /// </summary>
    private static string NormaliseName(string? name)
    {
        var trimmed = name?.Trim().ToLowerInvariant() ?? string.Empty;

        if (trimmed.Length == 0)
            throw new ValidationException("Give the server a short name, such as 'code-search'.");

        if (trimmed.Length > 64)
            throw new ValidationException("The name must be 64 characters or fewer.");

        if (!NamePattern().IsMatch(trimmed))
        {
            throw new ValidationException(
                "The name may use lower-case letters, digits and hyphens only — for example "
                + "'code-search'.");
        }

        return trimmed;
    }

    /// <summary>Falls back to the name, which is at least honest and never blank.</summary>
    private static string NormaliseDisplayName(string? displayName, string name)
    {
        var trimmed = displayName?.Trim();
        return string.IsNullOrEmpty(trimmed) ? name : trimmed;
    }

    /// <summary>
    /// An absolute http or https URL, and required — a server with no address
    /// cannot be searched, and switching one off is what the enabled flag is
    /// for.
    ///
    /// The app fetches this address on the server's behalf, so a scheme like
    /// <c>file:</c> or <c>ftp:</c> would turn an admin text box into a way to
    /// read things the server can reach and the administrator cannot.
    /// Restricting the scheme does not make this risk-free — an administrator
    /// can still point it at any internal host — but that is a deliberate,
    /// role-gated capability rather than an accident.
    /// </summary>
    private static string RequireEndpoint(string? endpoint)
    {
        var trimmed = endpoint?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            throw new ValidationException("Enter the server's address.");

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new ValidationException("Enter a full address, including http:// or https://.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ValidationException("The address must start with http:// or https://.");

        return uri.ToString();
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex NamePattern();
}
