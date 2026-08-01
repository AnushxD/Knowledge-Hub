using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

/// <summary>The MCP repository servers an administrator has added.</summary>
public interface IRepositorySourceSettingRepository
{
    /// <summary>Every server, oldest first, so the list does not reorder itself on edit.</summary>
    Task<IReadOnlyList<RepositorySourceSetting>> ListAsync(CancellationToken ct = default);

    /// <summary>Null when no server goes by that name.</summary>
    Task<RepositorySourceSetting?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Adds a server. The caller checks the name is free first — this returns
    /// null rather than throwing on a clash, so the Service can turn it into a
    /// message about the name instead of a 500.
    /// </summary>
    Task<RepositorySourceSetting?> CreateAsync(
        RepositorySourceSetting source,
        CancellationToken ct = default);

    /// <summary>
    /// Updates everything about a server except its name, which is its
    /// identity. Null when it no longer exists.
    /// </summary>
    Task<RepositorySourceSetting?> UpdateAsync(
        string name,
        string displayName,
        string description,
        string endpoint,
        string toolName,
        bool isEnabled,
        Guid updatedById,
        CancellationToken ct = default);

    /// <summary>False when there was nothing by that name to remove.</summary>
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);
}

internal sealed class RepositorySourceSettingRepository(DocHubDbContext db)
    : IRepositorySourceSettingRepository
{
    public async Task<IReadOnlyList<RepositorySourceSetting>> ListAsync(
        CancellationToken ct = default) =>
        await db.RepositorySourceSettings
            .AsNoTracking()
            .OrderBy(setting => setting.CreatedAt)
            .ToListAsync(ct);

    public Task<RepositorySourceSetting?> GetAsync(string name, CancellationToken ct = default) =>
        db.RepositorySourceSettings
            // No tracking: callers read this to decide how to build a source,
            // never to mutate the entity they were handed.
            .AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Name == name, ct);

    public async Task<RepositorySourceSetting?> CreateAsync(
        RepositorySourceSetting source,
        CancellationToken ct = default)
    {
        var taken = await db.RepositorySourceSettings
            .AnyAsync(setting => setting.Name == source.Name, ct);

        if (taken) return null;

        var now = DateTimeOffset.UtcNow;
        source.CreatedAt = now;
        source.UpdatedAt = now;

        db.RepositorySourceSettings.Add(source);
        await db.SaveChangesAsync(ct);

        return source;
    }

    public async Task<RepositorySourceSetting?> UpdateAsync(
        string name,
        string displayName,
        string description,
        string endpoint,
        string toolName,
        bool isEnabled,
        Guid updatedById,
        CancellationToken ct = default)
    {
        var existing = await db.RepositorySourceSettings
            .FirstOrDefaultAsync(setting => setting.Name == name, ct);

        if (existing is null) return null;

        existing.DisplayName = displayName;
        existing.Description = description;
        existing.Endpoint = endpoint;
        existing.ToolName = toolName;
        existing.IsEnabled = isEnabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedById = updatedById;

        await db.SaveChangesAsync(ct);

        return existing;
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default) =>
        await db.RepositorySourceSettings
            .Where(setting => setting.Name == name)
            .ExecuteDeleteAsync(ct) > 0;
}
