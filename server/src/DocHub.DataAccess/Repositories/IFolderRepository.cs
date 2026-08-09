using DocHub.DataAccess.Dtos;

namespace DocHub.DataAccess.Repositories;

/// <summary>
/// Persistence for the folder tree, which mirrors the repository's directories.
/// Services depend on this interface, never on EF Core, so the storage engine
/// stays swappable and Services stay testable.
///
/// There is no create, rename or delete: a directory exists here because it
/// exists in the repository, and <see cref="ReconcileAsync"/> is the only way
/// that changes.
/// </summary>
public interface IFolderRepository
{
    /// <summary>Every folder, each carrying a recursive document count.</summary>
    Task<IReadOnlyList<FolderDto>> GetAllAsync(CancellationToken ct = default);

    Task<FolderDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The folder plus its ancestors, root first — used for breadcrumbs.</summary>
    Task<IReadOnlyList<FolderDto>> GetBreadcrumbAsync(Guid id, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Makes the folder tree match the repository's directories exactly:
    /// creates what is missing, including intermediate levels, and removes what
    /// is no longer there along with the documents beneath it.
    /// </summary>
    /// <param name="paths">
    /// Every directory path in the tree, "/"-separated and relative to the
    /// configured sub-path. Must be the complete set — anything absent is taken
    /// to have left the repository, so a partial list deletes the difference.
    /// </param>
    /// <returns>Every folder's id by path, so the caller can place files without re-querying.</returns>
    Task<IReadOnlyDictionary<string, Guid>> ReconcileAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);
}
