using DocHub.Services.Folders;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

/// <summary>
/// Endpoints only — accepts and returns ViewModels, holds no business logic.
///
/// Read-only, and there is nothing to add: the tree is the repository's
/// directory structure. Creating a folder here would be undone by the next
/// sync, and the honest place to add one is a commit.
/// </summary>
[ApiController]
[Route("api/folders")]
[Produces("application/json")]
public sealed class FoldersController(IFolderService folders) : ControllerBase
{
    /// <summary>The whole folder tree, each node carrying a recursive document count.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FolderViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<FolderViewModel>> GetAll(CancellationToken ct) =>
        await folders.GetAllAsync(ct);
}
