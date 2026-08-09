using DocHub.Services.ViewModels;

namespace DocHub.Services.Folders;

/// <summary>
/// Reads the folder tree.
///
/// Read-only by design: the tree is the repository's directory structure, so
/// creating, renaming or deleting a folder here would either be a lie the next
/// sync undoes, or a write into somebody's repository. Both are worse than not
/// offering it.
/// </summary>
public interface IFolderService
{
    Task<IReadOnlyList<FolderViewModel>> GetAllAsync(CancellationToken ct = default);
}
