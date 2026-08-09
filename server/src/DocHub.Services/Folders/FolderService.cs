using DocHub.DataAccess.Repositories;
using DocHub.Services.ViewModels;

namespace DocHub.Services.Folders;

internal sealed class FolderService(IFolderRepository folders) : IFolderService
{
    public async Task<IReadOnlyList<FolderViewModel>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await folders.GetAllAsync(ct);
        return [.. all.Select(folder => folder.ToViewModel())];
    }
}
