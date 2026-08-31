using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.SourceControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Repository;

/// <summary>
/// The settings reader, plus the one thing only this layer may do to it: put a
/// change in force immediately.
///
/// Separate from <see cref="IRepositorySettingsReader"/> so that saving does not
/// depend on the caching implementation, and so a test can substitute one.
/// </summary>
internal interface IRepositorySettingsRefresher : IRepositorySettingsReader
{
    /// <summary>
    /// Re-reads the saved settings now, and returns what is in force after it.
    /// </summary>
    Task<RepositoryConfiguration> RefreshAsync(CancellationToken ct = default);
}

/// <summary>
/// The repository settings in force: what an administrator saved, laid over
/// what the deployment configured.
///
/// A singleton with a cached snapshot, not a per-request read. Every GitLab
/// call needs these, including ones on a background job and ones from a
/// property that cannot await, and a database round trip per call to answer a
/// question whose answer changes twice a year would be absurd.
///
/// Staleness is bounded three ways: saving refreshes it immediately, anything
/// that actually reaches GitLab awaits <see cref="GetAsync"/>, and that
/// re-reads once the snapshot is older than <see cref="Freshness"/>. The last
/// is what a second API instance relies on — it never sees the save, only the
/// row.
/// </summary>
internal sealed class StoredRepositorySettings(
    IServiceScopeFactory scopes,
    ISecretProtector protector,
    IOptions<GitLabOptions> options,
    ILogger<StoredRepositorySettings> logger) : IRepositorySettingsRefresher
{
    /// <summary>
    /// How long a snapshot is trusted. Short enough that a change made on one
    /// instance takes effect on another within a sync's notice, long enough
    /// that a busy ingestion backlog does not re-read a one-row table per file.
    /// </summary>
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(30);

    private readonly RepositoryConfiguration _configured =
        RepositoryConfiguration.From(options.Value);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private RepositoryConfiguration? _snapshot;

    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Whether the last load could not read the table. Kept so the warning is
    /// logged on the transition and not once per call — a database that is
    /// down is already being reported by the health check.
    /// </summary>
    private bool _lastLoadFailed;

    public RepositoryConfiguration Current => Volatile.Read(ref _snapshot) ?? _configured;

    public async ValueTask<RepositoryConfiguration> GetAsync(CancellationToken ct = default)
    {
        var snapshot = Volatile.Read(ref _snapshot);

        if (snapshot is not null && DateTimeOffset.UtcNow - _loadedAt < Freshness) return snapshot;

        await _gate.WaitAsync(ct);

        try
        {
            // Someone else may have loaded it while this call waited; the point
            // of the gate is one read, not one read each.
            snapshot = Volatile.Read(ref _snapshot);

            if (snapshot is not null && DateTimeOffset.UtcNow - _loadedAt < Freshness)
                return snapshot;

            return await LoadAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Re-reads the row now, and returns what is in force after it.
    ///
    /// Called by the administration service the moment it saves, so the answer
    /// it hands back to the screen — and the very next sync — describe the same
    /// repository the person just chose.
    /// </summary>
    public async Task<RepositoryConfiguration> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);

        try
        {
            return await LoadAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RepositoryConfiguration> LoadAsync(CancellationToken ct)
    {
        try
        {
            // Its own scope: this is a singleton, and the repository it needs
            // wraps a request-scoped DbContext.
            await using var scope = scopes.CreateAsyncScope();

            var settings = scope.ServiceProvider.GetRequiredService<IRepositorySettingsRepository>();
            var row = await settings.GetAsync(ct);

            var resolved = Overlay(row);

            Volatile.Write(ref _snapshot, resolved);
            _loadedAt = DateTimeOffset.UtcNow;
            _lastLoadFailed = false;

            return resolved;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Configuration is the fallback, not a failure: a hub whose
            // database is unreachable has larger problems than which repository
            // it mirrors, and pretending it is unconfigured would add a
            // misleading one.
            if (!_lastLoadFailed)
            {
                logger.LogWarning(
                    exception,
                    "Could not read the saved repository settings; using the configured ones.");
            }

            _lastLoadFailed = true;
            _loadedAt = DateTimeOffset.UtcNow;

            return Volatile.Read(ref _snapshot) ?? _configured;
        }
    }

    /// <summary>
    /// Merges the saved row onto configuration, field by field.
    ///
    /// An unset field means "whatever the deployment configured", so a box
    /// provisioned by environment variables keeps working after somebody
    /// changes only the branch in the UI. The sub-path is the exception and
    /// carries its own flag: empty is a real choice there — mirror the whole
    /// repository — and reading it as "unset" would quietly restore a
    /// configured sub-path the administrator had just cleared.
    /// </summary>
    private RepositoryConfiguration Overlay(RepositorySettings? row)
    {
        if (row is null) return _configured;

        return _configured with
        {
            BaseUrl = Prefer(row.BaseUrl, _configured.BaseUrl),
            ProjectPath = Prefer(row.ProjectPath, _configured.ProjectPath),
            Branch = Prefer(row.Branch, _configured.Branch),
            SubPath = row.HasSubPath ? row.SubPath.Trim() : _configured.SubPath,
            Token = Secret(row.ProtectedToken) ?? _configured.Token,
            WebhookSecret = Secret(row.ProtectedWebhookSecret) ?? _configured.WebhookSecret,
            Origin = RepositoryConfigurationOrigin.Saved,
        };
    }

    private static string Prefer(string saved, string configured) =>
        string.IsNullOrWhiteSpace(saved) ? configured : saved.Trim();

    /// <summary>
    /// A stored secret, or null to fall back to configuration. Null is also
    /// what unreadable ciphertext gives — a key ring that did not survive a
    /// recycle — because a secret nobody can decrypt is a secret nobody has.
    /// </summary>
    private string? Secret(string? ciphertext)
    {
        if (ciphertext is null) return null;

        var plaintext = protector.Unprotect(ciphertext);

        if (plaintext is null)
        {
            logger.LogWarning(
                "A saved repository secret could not be decrypted, so the configured value is in "
                + "force. The Data Protection key ring has changed; set the secret again under "
                + "Settings, and set Authentication:KeyPath so this cannot recur.");
        }

        return plaintext;
    }
}
