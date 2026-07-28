using DocHub.Services;
using DocHub.Services.Documents;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Produces("application/json")]
public sealed class DocumentsController(IDocumentService documents) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DocumentViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<DocumentViewModel>> Query(
        [FromQuery] DocumentQueryRequest request,
        CancellationToken ct) =>
        await documents.QueryAsync(request, ct);

    /// <summary>Library-wide counts for the dashboard.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<LibraryStatsViewModel>(StatusCodes.Status200OK)]
    public async Task<LibraryStatsViewModel> Stats(CancellationToken ct) =>
        await documents.GetStatsAsync(ct);

    /// <summary>Every tag in use, for the filter list.</summary>
    [HttpGet("tags")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<string>> Tags(CancellationToken ct) =>
        await documents.GetTagsAsync(ct);

    /// <summary>Everyone who owns at least one document, for the owner filter.</summary>
    [HttpGet("owners")]
    [ProducesResponseType<IReadOnlyList<UserViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<UserViewModel>> Owners(CancellationToken ct) =>
        await documents.GetOwnersAsync(ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentDetailViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DocumentDetailViewModel> Get(Guid id, CancellationToken ct) =>
        await documents.GetAsync(id, ct);

    /// <summary>Streams the stored file back.</summary>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var file = await documents.DownloadAsync(id, ct);

        // enableRangeProcessing lets the browser seek within a PDF preview
        // instead of refetching the whole file.
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    /// <summary>Uploads a new document into a folder.</summary>
    [HttpPost]
    [ProducesResponseType<DocumentViewModel>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentViewModel>> Upload(
        [FromQuery] Guid folderId,
        IFormFile file,
        CancellationToken ct)
    {
        var created = await documents.UploadAsync(folderId, ToUploadRequest(file), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Uploads a replacement file as a new version of an existing document.</summary>
    [HttpPost("{id:guid}/versions")]
    [ProducesResponseType<DocumentViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DocumentViewModel> AddVersion(
        Guid id,
        IFormFile file,
        [FromQuery] string? note,
        CancellationToken ct) =>
        await documents.AddVersionAsync(id, ToUploadRequest(file, note), ct);

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<DocumentViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DocumentViewModel> Update(
        Guid id,
        [FromBody] UpdateDocumentRequest request,
        CancellationToken ct) =>
        await documents.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/move")]
    [ProducesResponseType<DocumentViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DocumentViewModel> Move(
        Guid id,
        [FromBody] MoveDocumentRequest request,
        CancellationToken ct) =>
        await documents.MoveAsync(id, request.FolderId, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await documents.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Adapts ASP.NET's IFormFile to the transport-agnostic request the service
    /// expects, so business logic never depends on the web stack.
    /// </summary>
    private static UploadRequest ToUploadRequest(IFormFile? file, string? note = null)
    {
        if (file is null || file.Length == 0)
            throw new ValidationException("A file is required.");

        return new UploadRequest(
            file.OpenReadStream(),
            file.FileName,
            string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            file.Length,
            note);
    }
}
