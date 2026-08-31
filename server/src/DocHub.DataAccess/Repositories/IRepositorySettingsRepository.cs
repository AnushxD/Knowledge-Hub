using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

/// <summary>
/// The one row saying which repository the hub mirrors, or nothing at all when
/// the deployment's configuration has never been overridden.
/// </summary>
public interface IRepositorySettingsRepository
{
    /// <summary>
    /// Null when nobody has changed the repository in the UI, which is the
    /// ordinary state of a box configured by environment variables.
    /// </summary>
    Task<RepositorySettings?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes the row, creating it the first time. The caller supplies every
    /// field: this is one setting with several parts, not several settings.
    /// </summary>
    Task<RepositorySettings> SaveAsync(RepositorySettings settings, CancellationToken ct = default);
}

internal sealed class RepositorySettingsRepository(DocHubDbContext db)
    : IRepositorySettingsRepository
{
    public Task<RepositorySettings?> GetAsync(CancellationToken ct = default) =>
        db.RepositorySettings
            // No tracking: every caller reads this to decide how to reach
            // GitLab, never to mutate the entity it was handed.
            .AsNoTracking()
            .FirstOrDefaultAsync(settings => settings.Id == RepositorySettings.SingletonId, ct);

    public async Task<RepositorySettings> SaveAsync(
        RepositorySettings settings,
        CancellationToken ct = default)
    {
        var existing = await db.RepositorySettings
            .FirstOrDefaultAsync(row => row.Id == RepositorySettings.SingletonId, ct);

        if (existing is null)
        {
            settings.Id = RepositorySettings.SingletonId;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            db.RepositorySettings.Add(settings);
            await db.SaveChangesAsync(ct);

            return settings;
        }

        existing.BaseUrl = settings.BaseUrl;
        existing.ProjectPath = settings.ProjectPath;
        existing.Branch = settings.Branch;
        existing.SubPath = settings.SubPath;
        existing.HasSubPath = settings.HasSubPath;
        existing.ProtectedToken = settings.ProtectedToken;
        existing.ProtectedWebhookSecret = settings.ProtectedWebhookSecret;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedById = settings.UpdatedById;

        await db.SaveChangesAsync(ct);

        return existing;
    }
}
