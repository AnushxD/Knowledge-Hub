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

    public async Task<IReadOnlyDictionary<string, Guid>> ReconcileAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        // Every ancestor of a wanted path is itself wanted, even when the tree
        // listing never named it on its own. GitLab lists "a/b/c" as a tree
        // entry, but a repository containing only "a/b/c/file.md" and nothing
        // directly in "a" still needs "a" to hang the branch from.
        var wanted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var trimmed = path.Trim('/');
            if (trimmed.Length == 0) continue;

            for (var cut = trimmed.IndexOf('/');
                 cut >= 0;
                 cut = trimmed.IndexOf('/', cut + 1))
            {
                wanted.Add(trimmed[..cut]);
            }

            wanted.Add(trimmed);
        }

        var existing = await db.Folders.ToDictionaryAsync(
            folder => folder.Path, folder => folder, StringComparer.Ordinal, ct);

        // Shallowest first, so a folder's parent always exists by the time it
        // is created and the ParentId can be filled in on the spot.
        var now = DateTimeOffset.UtcNow;
        var created = new List<Folder>();

        foreach (var path in wanted.OrderBy(CountSegments).ThenBy(path => path, StringComparer.Ordinal))
        {
            if (existing.ContainsKey(path)) continue;

            var cut = path.LastIndexOf('/');
            var name = cut < 0 ? path : path[(cut + 1)..];
            var parentPath = cut < 0 ? null : path[..cut];

            var folder = new Folder
            {
                // Version 7 GUIDs are time-ordered, so inserts stay at the right
                // of the B-tree instead of fragmenting it like random v4 values.
                Id = Guid.CreateVersion7(),
                ParentId = parentPath is null ? null : existing[parentPath].Id,
                Name = name,
                Path = path,
                CreatedAt = now,
                UpdatedAt = now,
            };

            existing[path] = folder;
            created.Add(folder);
        }

        if (created.Count > 0) db.Folders.AddRange(created);

        // Anything left is a directory that has gone from the repository. The
        // cascade takes its subtree and the documents in it. Folders created
        // moments ago cannot appear here — they were all added from `wanted`.
        var departed = existing.Values
            .Where(folder => !wanted.Contains(folder.Path))
            .ToList();

        if (departed.Count > 0) db.Folders.RemoveRange(departed);

        await db.SaveChangesAsync(ct);

        return existing
            .Where(pair => wanted.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Id, StringComparer.Ordinal);
    }

    private Task<int> CountDescendantDocumentsAsync(string path, CancellationToken ct) =>
        db.Documents.CountAsync(
            document => document.Folder!.Path == path
                || EF.Functions.Like(document.Folder!.Path, path + "/%"),
            ct);

    private static int CountSegments(string path) => path.Count(character => character == '/');

    private static bool IsSelfOrDescendant(Folder candidate, Folder ancestor) =>
        candidate.Path == ancestor.Path || candidate.Path.StartsWith(ancestor.Path + "/", StringComparison.Ordinal);

    private static FolderDto ToDto(Folder folder, int documentCount) => new(
        folder.Id,
        folder.ParentId,
        folder.Name,
        folder.Path,
        documentCount,
        folder.CreatedAt,
        folder.UpdatedAt);
}
