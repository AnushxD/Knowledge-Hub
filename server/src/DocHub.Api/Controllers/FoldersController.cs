using DocHub.Services.Folders;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

/// <summary>
/// Endpoints only — accepts and returns ViewModels, holds no business logic.
/// Every rule (name uniqueness, cascade behaviour) lives in the service.
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

    [HttpPost]
    [ProducesResponseType<FolderViewModel>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderViewModel>> Create(
        [FromBody] CreateFolderRequest request,
        CancellationToken ct)
    {
        var created = await folders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetAll), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<FolderViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<FolderViewModel> Rename(
        Guid id,
        [FromBody] RenameFolderRequest request,
        CancellationToken ct) =>
        await folders.RenameAsync(id, request, ct);

    /// <summary>Deletes the folder, its subtree, and every stored file beneath it.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await folders.DeleteAsync(id, ct);
        return NoContent();
    }
}
