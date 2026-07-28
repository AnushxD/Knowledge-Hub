using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

internal sealed class FolderRepository(DocHubDbContext db) : IFolderRepository
{
    public async Task<IReadOnlyList<FolderDto>> GetAllAsync(CancellationToken ct = default)
    {
        var folders = await db.Folders
            .AsNoTracking()
            .OrderBy(folder => folder.Path)
            .ToListAsync(ct);

        var directCounts = await db.Documents
            .AsNoTracking()
            .GroupBy(document => document.FolderId)
            .Select(group => new { FolderId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.FolderId, row => row.Count, ct);

        // Counts are recursive — a folder shows everything beneath it, which is
        // what the sidebar displays. Done in memory against the materialised
        // paths: folder counts are in the hundreds, so a recursive CTE would
        // cost more than it saves.
        return folders
            .Select(folder => ToDto(
                folder,
                folders
                    .Where(candidate => IsSelfOrDescendant(candidate, folder))
                    .Sum(candidate => directCounts.GetValueOrDefault(candidate.Id))))
            .ToList();
    }

    public async Task<FolderDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null) return null;

        var count = await CountDescendantDocumentsAsync(folder.Path, ct);
        return ToDto(folder, count);
    }

    public async Task<IReadOnlyList<FolderDto>> GetBreadcrumbAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null) return [];

        // Ancestors are exactly the folders whose path prefixes this one. The
        // "/" guard stops "Eng" matching "Engineering".
        var ancestors = await db.Folders
            .AsNoTracking()
            .Where(candidate =>
                candidate.Path == folder.Path ||
                EF.Functions.Like(folder.Path, candidate.Path + "/%"))
            .OrderBy(candidate => candidate.Path.Length)
            .ToListAsync(ct);

        return ancestors.Select(ancestor => ToDto(ancestor, 0)).ToList();
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        db.Folders.AnyAsync(folder => folder.Id == id, ct);

    public Task<bool> NameTakenAsync(
        Guid? parentId,
        string name,
        Guid? excludingId = null,
        CancellationToken ct = default) =>
        db.Folders.AnyAsync(
            folder => folder.ParentId == parentId
                && folder.Name.ToLower() == name.ToLower()
                && (excludingId == null || folder.Id != excludingId),
            ct);

    public async Task<FolderDto> CreateAsync(
        Guid? parentId,
        string name,
        Guid ownerId,
        CancellationToken ct = default)
    {
        var parentPath = parentId is null
            ? null
            : await db.Folders
                .Where(folder => folder.Id == parentId)
                .Select(folder => folder.Path)
                .FirstOrDefaultAsync(ct);

        if (parentId is not null && parentPath is null)
            throw new InvalidOperationException($"Parent folder {parentId} does not exist.");

        var now = DateTimeOffset.UtcNow;
        var folder = new Folder
        {
            // Version 7 GUIDs are time-ordered, so inserts stay at the right of
            // the B-tree instead of fragmenting it like random v4 values do.
            Id = Guid.CreateVersion7(),
            ParentId = parentId,
            Name = name,
            Path = parentPath is null ? name : $"{parentPath}/{name}",
            OwnerId = ownerId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Folders.Add(folder);
        await db.SaveChangesAsync(ct);
        return ToDto(folder, 0);
    }

    public async Task<FolderDto?> RenameAsync(Guid id, string name, CancellationToken ct = default)
    {
        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null) return null;

        var oldPath = folder.Path;
        var newPath = folder.ParentId is null
            ? name
            : $"{oldPath[..oldPath.LastIndexOf('/')]}/{name}";

        folder.Name = name;
        folder.Path = newPath;
        folder.UpdatedAt = DateTimeOffset.UtcNow;

        // The materialised path is denormalised, so the whole subtree has to be
        // rewritten in the same transaction as the rename.
        var descendants = await db.Folders
            .Where(candidate => EF.Functions.Like(candidate.Path, oldPath + "/%"))
            .ToListAsync(ct);

        foreach (var descendant in descendants)
        {
            descendant.Path = string.Concat(newPath, descendant.Path.AsSpan(oldPath.Length));
            descendant.UpdatedAt = folder.UpdatedAt;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(folder, await CountDescendantDocumentsAsync(newPath, ct));
    }

    public async Task<IReadOnlyList<string>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null) return [];

        var scope = await db.Folders
            .Where(candidate =>
                candidate.Path == folder.Path ||
                EF.Functions.Like(candidate.Path, folder.Path + "/%"))
            .Select(candidate => candidate.Id)
            .ToListAsync(ct);

        // Collect blob paths before the cascade removes the rows — otherwise the
        // files are orphaned in storage with nothing left pointing at them.
        var currentBlobs = await db.Documents
            .Where(document => scope.Contains(document.FolderId))
            .Select(document => document.StoragePath)
            .ToListAsync(ct);

        var versionBlobs = await db.DocumentVersions
            .Where(version => scope.Contains(version.Document!.FolderId))
            .Select(version => version.StoragePath)
            .ToListAsync(ct);

        db.Folders.Remove(folder);
        await db.SaveChangesAsync(ct);

        return currentBlobs.Concat(versionBlobs).Distinct().ToList();
    }

    private Task<int> CountDescendantDocumentsAsync(string path, CancellationToken ct) =>
        db.Documents.CountAsync(
            document => document.Folder!.Path == path
                || EF.Functions.Like(document.Folder!.Path, path + "/%"),
            ct);

    private static bool IsSelfOrDescendant(Folder candidate, Folder ancestor) =>
        candidate.Path == ancestor.Path || candidate.Path.StartsWith(ancestor.Path + "/", StringComparison.Ordinal);

    private static FolderDto ToDto(Folder folder, int documentCount) => new(
        folder.Id,
        folder.ParentId,
        folder.Name,
        folder.Path,
        folder.OwnerId,
        documentCount,
        folder.CreatedAt,
        folder.UpdatedAt);
}
