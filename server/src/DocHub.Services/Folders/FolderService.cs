using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Services.Activity;
using DocHub.Integrations.Storage;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Folders;

internal sealed class FolderService(
    IFolderRepository folders,
    IFileStorage storage,
    IActivityLog activity,
    ICurrentUser currentUser,
    ILogger<FolderService> logger) : IFolderService
{
    private const int MaxNameLength = 200;

    public async Task<IReadOnlyList<FolderViewModel>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await folders.GetAllAsync(ct);
        return [.. all.Select(folder => folder.ToViewModel())];
    }

    public async Task<FolderViewModel> CreateAsync(
        CreateFolderRequest request,
        CancellationToken ct = default)
    {
        var name = NormaliseName(request.Name);

        if (request.ParentId is { } parentId && !await folders.ExistsAsync(parentId, ct))
            throw new NotFoundException("Folder", parentId);

        // Sibling names must stay unique, otherwise a materialised path no
        // longer identifies a single folder.
        if (await folders.NameTakenAsync(request.ParentId, name, ct: ct))
            throw new ValidationException($"A folder named '{name}' already exists here.");

        var created = await folders.CreateAsync(request.ParentId, name, currentUser.Id, ct);
        logger.LogInformation("Created folder {FolderId} at {Path}", created.Id, created.Path);

        await activity.RecordAsync(ActivityType.FolderCreated, created.Name, ct: ct);

        return created.ToViewModel();
    }

    public async Task<FolderViewModel> RenameAsync(
        Guid id,
        RenameFolderRequest request,
        CancellationToken ct = default)
    {
        var name = NormaliseName(request.Name);

        var existing = await folders.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Folder", id);

        if (await folders.NameTakenAsync(existing.ParentId, name, excludingId: id, ct: ct))
            throw new ValidationException($"A folder named '{name}' already exists here.");

        var renamed = await folders.RenameAsync(id, name, ct)
            ?? throw new NotFoundException("Folder", id);

        return renamed.ToViewModel();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (!await folders.ExistsAsync(id, ct))
            throw new NotFoundException("Folder", id);

        // The repository removes the subtree and hands back every blob those
        // documents owned. Deleting the rows first means a storage failure
        // leaves orphaned files rather than rows pointing at missing files —
        // the recoverable direction of the two.
        // The name has to be read before the row goes, for the same reason a
        // deleted document's title does.
        var name = (await folders.GetAllAsync(ct))
            .FirstOrDefault(folder => folder.Id == id)?.Name ?? "a folder";

        var orphanedBlobs = await folders.DeleteAsync(id, ct);
        await storage.DeleteManyAsync(orphanedBlobs, ct);

        await activity.RecordAsync(ActivityType.FolderDeleted, name, ct: ct);

        logger.LogInformation(
            "Deleted folder {FolderId} and {BlobCount} stored files", id, orphanedBlobs.Count);
    }

    private static string NormaliseName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ValidationException("Folder name is required.");

        if (trimmed.Length > MaxNameLength)
            throw new ValidationException($"Folder name cannot exceed {MaxNameLength} characters.");

        // "/" is the path separator for the materialised path, so a name
        // containing one would corrupt every descendant's path.
        if (trimmed.Contains('/'))
            throw new ValidationException("Folder name cannot contain '/'.");

        return trimmed;
    }
}
