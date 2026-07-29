using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

/// <summary>Reads and writes the administrator-set repository source address.</summary>
public interface IRepositorySourceSettingRepository
{
    /// <summary>Null when nobody has overridden configuration.</summary>
    Task<RepositorySourceSetting?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Creates the row or updates it in place.</summary>
    Task<RepositorySourceSetting> SaveAsync(
        string name,
        string? endpoint,
        bool isEnabled,
        Guid updatedById,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the override, so configuration takes over again. Distinct from
    /// saving an empty endpoint, which is an administrator deliberately
    /// clearing the address.
    /// </summary>
    Task<bool> ClearAsync(string name, CancellationToken ct = default);
}

internal sealed class RepositorySourceSettingRepository(DocHubDbContext db)
    : IRepositorySourceSettingRepository
{
    public Task<RepositorySourceSetting?> GetAsync(string name, CancellationToken ct = default) =>
        db.RepositorySourceSettings
            // No tracking: callers read this to decide how to build a source,
            // never to mutate the entity they were handed.
            .AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Name == name, ct);

    public async Task<RepositorySourceSetting> SaveAsync(
        string name,
        string? endpoint,
        bool isEnabled,
        Guid updatedById,
        CancellationToken ct = default)
    {
        var existing = await db.RepositorySourceSettings
            .FirstOrDefaultAsync(setting => setting.Name == name, ct);

        if (existing is null)
        {
            existing = new RepositorySourceSetting { Name = name };
            db.RepositorySourceSettings.Add(existing);
        }

        existing.Endpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
        existing.IsEnabled = isEnabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedById = updatedById;

        await db.SaveChangesAsync(ct);

        return existing;
    }

    public async Task<bool> ClearAsync(string name, CancellationToken ct = default)
    {
        var existing = await db.RepositorySourceSettings
            .FirstOrDefaultAsync(setting => setting.Name == name, ct);

        if (existing is null) return false;

        db.RepositorySourceSettings.Remove(existing);
        await db.SaveChangesAsync(ct);

        return true;
    }
}
