using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.SourceControl;
using DocHub.Services.Activity;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Repository;

/// <summary>
/// Reading and changing which repository the hub mirrors.
///
/// The rules live here rather than in the controller because they are
/// decisions: what counts as a usable address, that a blank field means "use
/// what the deployment configured", that a secret is replaced rather than
/// echoed, and that repointing the hub replaces the library at the next sync.
/// </summary>
public interface IRepositorySettingsAdmin
{
    /// <summary>
    /// The settings in force, with the secrets described rather than returned.
    /// </summary>
    Task<RepositorySettingsViewModel> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the settings and puts them in force immediately — the next sync,
    /// webhook and file fetch use them, with no restart.
    /// </summary>
    Task<RepositorySettingsViewModel> SaveAsync(
        UpdateRepositorySettingsRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Reads the repository described by <paramref name="request"/> without
    /// saving it, so a mistake is caught while it is still being typed. A null
    /// token means "the one already held", exactly as it does when saving.
    /// </summary>
    Task<RepositoryConnectionViewModel> TestAsync(
        UpdateRepositorySettingsRequest request,
        CancellationToken ct = default);
}

internal sealed class RepositorySettingsAdmin(
    IRepositorySettingsRepository settings,
    IRepositorySettingsRefresher inForce,
    IRepositoryConnectionProbe probe,
    ISecretProtector protector,
    IActivityLog activity,
    ICurrentUser currentUser,
    ILogger<RepositorySettingsAdmin> logger) : IRepositorySettingsAdmin
{
    public async Task<RepositorySettingsViewModel> GetAsync(CancellationToken ct = default)
    {
        var row = await settings.GetAsync(ct);
        var current = await inForce.GetAsync(ct);

        return ToViewModel(row, current);
    }

    public async Task<RepositorySettingsViewModel> SaveAsync(
        UpdateRepositorySettingsRequest request,
        CancellationToken ct = default)
    {
        var existing = await settings.GetAsync(ct);

        var baseUrl = RequireBaseUrl(request.BaseUrl);
        var projectPath = RequireProjectPath(request.ProjectPath);
        var branch = RequireBranch(request.Branch);

        var saved = await settings.SaveAsync(
            new RepositorySettings
            {
                BaseUrl = baseUrl,
                ProjectPath = projectPath,
                Branch = branch,
                SubPath = (request.SubPath ?? string.Empty).Trim().Trim('/'),

                // Set on every save, whatever it was set to. Empty here means
                // "mirror the whole repository", which is a choice and not an
                // absence — without this flag, clearing the field would fall
                // back to a configured sub-path instead of honouring it.
                HasSubPath = true,

                ProtectedToken = Resolve(request.Token, existing?.ProtectedToken),
                ProtectedWebhookSecret =
                    Resolve(request.WebhookSecret, existing?.ProtectedWebhookSecret),
                UpdatedById = currentUser.Id,
            },
            ct);

        // In force before this method returns, so the answer the screen draws
        // and the repository the next question is asked of are the same one.
        var current = await inForce.RefreshAsync(ct);

        // One entry, naming the repository rather than the fields: the trail
        // records that the hub was pointed somewhere, and the target is where.
        // Never the token — an audit trail is read by more people than the
        // setting it describes.
        await activity.RecordAsync(
            ActivityType.Updated,
            $"Repository → {projectPath}@{branch}",
            targetId: null,
            ct);

        logger.LogInformation(
            "User {UserId} pointed the hub at {Project}@{Branch} on {BaseUrl} (sub-path: {SubPath})",
            currentUser.Id, projectPath, branch, baseUrl,
            saved.SubPath.Length == 0 ? "(whole repository)" : saved.SubPath);

        return ToViewModel(saved, current);
    }

    public async Task<RepositoryConnectionViewModel> TestAsync(
        UpdateRepositorySettingsRequest request,
        CancellationToken ct = default)
    {
        var current = await inForce.GetAsync(ct);

        var candidate = current with
        {
            BaseUrl = (request.BaseUrl ?? string.Empty).Trim(),
            ProjectPath = (request.ProjectPath ?? string.Empty).Trim().Trim('/'),
            Branch = (request.Branch ?? string.Empty).Trim(),
            SubPath = (request.SubPath ?? string.Empty).Trim().Trim('/'),

            // Null means "test with the token already held", so an
            // administrator changing only the branch does not have to paste a
            // credential back in to check their change.
            Token = request.Token ?? current.Token,
        };

        if (string.IsNullOrWhiteSpace(candidate.Branch))
        {
            throw new ValidationException("Name the branch to mirror, such as 'main'.");
        }

        var connection = await probe.ProbeAsync(candidate, ct);

        return new RepositoryConnectionViewModel(
            connection.IsReachable,
            connection.ProjectFound,
            connection.BranchFound,
            connection.SubPathFound,
            connection.UsedToken,
            connection.Detail,
            connection.ProjectName,
            connection.DefaultBranch,
            connection.WebUrl);
    }

    /// <summary>
    /// What to store for a secret, given what the caller sent.
    ///
    /// Null keeps what is there, empty clears it, anything else replaces it.
    /// The distinction matters: a screen that never shows a token has no way to
    /// send it back, so "unchanged" has to be expressible.
    /// </summary>
    private string? Resolve(string? supplied, string? existing)
    {
        if (supplied is null) return existing;

        var trimmed = supplied.Trim();

        return trimmed.Length == 0 ? null : protector.Protect(trimmed);
    }

    private RepositorySettingsViewModel ToViewModel(
        RepositorySettings? row,
        RepositoryConfiguration current)
    {
        // "Unreadable" is decided from the row, not from what is in force: the
        // effective token falls back to the configured one, and reporting that
        // as a working saved token would hide the very problem worth naming.
        var tokenUnreadable = Unreadable(row?.ProtectedToken);
        var secretUnreadable = Unreadable(row?.ProtectedWebhookSecret);

        return new RepositorySettingsViewModel(
            current.BaseUrl,
            current.ProjectPath,
            current.Branch,
            current.SubPath,
            HasToken: !string.IsNullOrWhiteSpace(current.Token),
            HasWebhookSecret: !string.IsNullOrWhiteSpace(current.WebhookSecret),
            tokenUnreadable,
            secretUnreadable,
            current.IsConfigured,
            IsSaved: row is not null,
            row?.UpdatedAt);
    }

    private bool Unreadable(string? ciphertext) =>
        ciphertext is not null && protector.Unprotect(ciphertext) is null;

    private static string RequireBaseUrl(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new ValidationException(
                "The instance address must be an absolute http or https URL, such as "
                + "'https://gitlab.example.org'.");
        }

        return trimmed.TrimEnd('/');
    }

    private static string RequireProjectPath(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().Trim('/');

        if (trimmed.Length == 0)
        {
            throw new ValidationException(
                "Give the namespaced project path, as GitLab spells it — 'team/docs'.");
        }

        // A URL pasted out of the browser is the mistake this catches: it looks
        // right, and GitLab answers 404 for it with no hint as to why.
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            throw new ValidationException(
                "The project path is the part after the instance address — 'team/docs', not the "
                + "whole URL.");
        }

        return trimmed;
    }

    private static string RequireBranch(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new ValidationException("Name the branch to mirror, such as 'main'.");
        }

        return trimmed;
    }
}
