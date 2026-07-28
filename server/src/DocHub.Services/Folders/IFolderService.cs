using DocHub.Services.ViewModels;

namespace DocHub.Services.Folders;

public interface IFolderService
{
    Task<IReadOnlyList<FolderViewModel>> GetAllAsync(CancellationToken ct = default);

    Task<FolderViewModel> CreateAsync(CreateFolderRequest request, CancellationToken ct = default);

    Task<FolderViewModel> RenameAsync(Guid id, RenameFolderRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
