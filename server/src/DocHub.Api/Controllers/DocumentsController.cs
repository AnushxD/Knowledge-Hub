using DocHub.Api.Infrastructure.Auth;
using DocHub.Services;
using DocHub.Services.Documents;
using DocHub.Services.Ingestion;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Produces("application/json")]
public sealed class DocumentsController(
    IDocumentService documents,
    IIngestionService ingestion) : ControllerBase
{
    /// <summary>File types the ingestion pipeline can make searchable.</summary>
    [HttpGet("supported-types")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public IReadOnlyList<string> SupportedTypes() => ingestion.SupportedExtensions;

    /// <summary>
    /// Puts a document back through ingestion. Backs the retry action on a
    /// failed document, and re-indexing after the chunking settings change.
    /// </summary>
    [Authorize(Policy = Policies.Contribute)]
    [HttpPost("{id:guid}/reindex")]
    [ProducesResponseType<DocumentViewModel>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentViewModel>> Reindex(Guid id, CancellationToken ct) =>
        Accepted(await ingestion.RequeueAsync(id, ct));

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

    /// <summary>
    /// Streams the stored file back.
    /// </summary>
    /// <param name="inline">
    /// Serve for display rather than download. Passing a file name to
    /// <c>File(...)</c> sets <c>Content-Disposition: attachment</c>, which makes
    /// the browser save the file instead of showing it — the opposite of what an
    /// embedded preview needs, so inline omits the name.
    ///
    /// Honoured only for the types in <see cref="InlineContentTypes"/>. Anything
    /// else falls back to a download **on purpose**: this endpoint is
    /// same-origin with the session cookie, so displaying arbitrary uploaded
    /// content here would let an uploaded SVG or HTML file run script against a
    /// signed-in reader's session.
    /// </param>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        Guid id,
        [FromQuery] bool inline,
        CancellationToken ct)
    {
        var file = await documents.DownloadAsync(id, ct);

        if (!inline || !InlineContentTypes.Contains(file.ContentType))
        {
            // enableRangeProcessing lets the browser seek within a PDF preview
            // instead of refetching the whole file.
            return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
        }

        // What actually makes this safe is the allow-list above — inert formats
        // only, no SVG and no HTML — together with nosniff, so the browser
        // cannot be talked into treating a PDF as a document that scripts.
        //
        // Note for anyone tempted to add `Content-Security-Policy: sandbox`
        // here: it does not harden this response, it disables it. A sandboxed
        // PDF cannot be shown by the browser's built-in viewer, so Chrome
        // silently falls back to downloading the file and the preview goes
        // blank. Origin isolation comes from CORP instead.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";

        // Stated rather than implied. Omitting Content-Disposition happens to
        // default to inline today, but saying so leaves nothing to a heuristic.
        Response.Headers.ContentDisposition = "inline";

        return File(file.Content, file.ContentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Types safe to render in the browser on our own origin.
    ///
    /// Deliberately a short allow-list of inert formats. <c>image/svg+xml</c> is
    /// absent because an SVG is a script host, and no <c>text/*</c> type appears
    /// because the client fetches text over XHR and renders it itself rather
    /// than pointing a frame at this endpoint.
    /// </summary>
    private static readonly HashSet<string> InlineContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/png",
            "image/jpeg",
            "image/gif",
            "image/webp",
            "image/bmp",
        };

    /// <summary>Uploads a new document into a folder.</summary>
    [Authorize(Policy = Policies.Contribute)]
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
    [Authorize(Policy = Policies.Contribute)]
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

    [Authorize(Policy = Policies.Contribute)]
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<DocumentViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DocumentViewModel> Update(
        Guid id,
        [FromBody] UpdateDocumentRequest request,
        CancellationToken ct) =>
        await documents.UpdateAsync(id, request, ct);

    [Authorize(Policy = Policies.Contribute)]
    [HttpPost("{id:guid}/move")]
    [ProducesResponseType<DocumentViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DocumentViewModel> Move(
        Guid id,
        [FromBody] MoveDocumentRequest request,
        CancellationToken ct) =>
        await documents.MoveAsync(id, request.FolderId, ct);

    [Authorize(Policy = Policies.Contribute)]
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
