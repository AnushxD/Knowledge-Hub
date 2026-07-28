using DocHub.DataAccess.Dtos;

namespace DocHub.DataAccess.Repositories;

/// <summary>
/// Persistence for the folder tree. Services depend on this interface, never on
/// EF Core, so the storage engine stays swappable and Services stay testable.
/// </summary>
public interface IFolderRepository
{
    /// <summary>Every folder, each carrying a recursive document count.</summary>
    Task<IReadOnlyList<FolderDto>> GetAllAsync(CancellationToken ct = default);

    Task<FolderDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The folder plus its ancestors, root first — used for breadcrumbs.</summary>
    Task<IReadOnlyList<FolderDto>> GetBreadcrumbAsync(Guid id, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>True when a sibling already uses this name.</summary>
    Task<bool> NameTakenAsync(Guid? parentId, string name, Guid? excludingId = null, CancellationToken ct = default);

    Task<FolderDto> CreateAsync(Guid? parentId, string name, Guid ownerId, CancellationToken ct = default);

    /// <summary>Renames the folder and rewrites the materialised path of its subtree.</summary>
    Task<FolderDto?> RenameAsync(Guid id, string name, CancellationToken ct = default);

    /// <summary>Deletes the folder and everything beneath it. Returns the blob paths freed.</summary>
    Task<IReadOnlyList<string>> DeleteAsync(Guid id, CancellationToken ct = default);
}
