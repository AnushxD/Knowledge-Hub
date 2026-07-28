using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Storage;
using DocHub.Services.Ingestion;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Documents;

internal sealed class DocumentService(
    IDocumentRepository documents,
    IFolderRepository folders,
    IChunkRepository chunks,
    IFileStorage storage,
    IIngestionQueue ingestion,
    ICurrentUser currentUser,
    ILogger<DocumentService> logger) : IDocumentService
{
    /// <summary>Mirrors the client-side guard; the server is the one that counts.</summary>
    private const long MaxUploadBytes = 25 * 1024 * 1024;

    /// <summary>
    /// Executable and script types are refused outright. This is a knowingly
    /// coarse guard for phase 1 — real content inspection and a virus scan
    /// belong in the phase 5 security hardening.
    /// </summary>
    private static readonly HashSet<string> BlockedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "exe", "dll", "bat", "cmd", "com", "msi", "sh", "ps1", "scr", "jar",
        };

    public async Task<IReadOnlyList<DocumentViewModel>> QueryAsync(
        DocumentQueryRequest request,
        CancellationToken ct = default)
    {
        var statuses = request.Status?
            .Select(Mapping.ParseStatus)
            .Where(status => status is not null)
            .Select(status => status!.Value)
            .ToList();

        var query = new DocumentQueryDto
        {
            FolderId = request.FolderId,
            Recursive = request.Recursive,
            Text = request.Text,
            Statuses = statuses,
            Extensions = request.Extension?.Select(e => e.TrimStart('.').ToLowerInvariant()).ToList(),
            Tags = request.Tag,
            OwnerId = request.OwnerId,
            StarredOnly = request.StarredOnly,
            Sort = Mapping.ParseSort(request.Sort),
            Skip = Math.Max(0, request.Skip),
            // Clamped so a caller cannot ask for the entire library in one page.
            Take = Math.Clamp(request.Take, 1, 500),
        };

        var results = await documents.QueryAsync(query, ct);
        return [.. results.Select(document => document.ToViewModel())];
    }

    public async Task<DocumentDetailViewModel> GetAsync(Guid id, CancellationToken ct = default)
    {
        var detail = await documents.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Document", id);

        // Empty for anything that has not finished ingestion, which is also
        // exactly when the client shows the pipeline state instead of a preview.
        var sections = await chunks.GetForDocumentAsync(id, ct);

        return detail.ToViewModel(sections);
    }

    public async Task<DocumentContent> DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var detail = await documents.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Document", id);

        var file = await storage.OpenReadAsync(detail.StoragePath, ct)
            // The row exists but the blob does not — a genuine inconsistency
            // worth logging loudly rather than reporting as a plain 404.
            ?? throw new InvalidOperationException(
                $"Document {id} points at missing blob '{detail.StoragePath}'.");

        return new DocumentContent(
            file.Content,
            file.ContentType,
            detail.Document.FileName,
            file.SizeBytes);
    }

    public async Task<DocumentViewModel> UploadAsync(
        Guid folderId,
        UploadRequest request,
        CancellationToken ct = default)
    {
        if (!await folders.ExistsAsync(folderId, ct))
            throw new NotFoundException("Folder", folderId);

        var (fileName, extension) = ValidateUpload(request);

        // Storage first: a blob with no row is a harmless orphan, whereas a row
        // with no blob is a broken document the user can see and click.
        var storagePath = await storage.SaveAsync(
            request.Content, fileName, request.ContentType, ct);

        try
        {
            var created = await documents.CreateAsync(
                new NewDocumentDto
                {
                    FolderId = folderId,
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    FileName = fileName,
                    Extension = extension,
                    ContentType = request.ContentType,
                    SizeBytes = request.SizeBytes,
                    StoragePath = storagePath,
                    OwnerId = currentUser.Id,
                },
                ct);

            logger.LogInformation(
                "Uploaded document {DocumentId} ({FileName}) to folder {FolderId}",
                created.Id, fileName, folderId);

            // Queued rather than awaited: extracting and embedding a document
            // takes seconds to minutes, and the upload response should not.
            ingestion.Enqueue(created.Id);

            return created.ToViewModel();
        }
        catch
        {
            // Do not leave a blob behind for a document that was never created.
            await storage.DeleteManyAsync([storagePath], ct);
            throw;
        }
    }

    public async Task<DocumentViewModel> AddVersionAsync(
        Guid id,
        UploadRequest request,
        CancellationToken ct = default)
    {
        if (await documents.GetStoragePathAsync(id, ct) is null)
            throw new NotFoundException("Document", id);

        var (fileName, _) = ValidateUpload(request);

        var storagePath = await storage.SaveAsync(
            request.Content, fileName, request.ContentType, ct);

        try
        {
            var updated = await documents.AddVersionAsync(
                id, storagePath, request.SizeBytes, request.Note, currentUser.Id, ct)
                ?? throw new NotFoundException("Document", id);

            // The previous blob is deliberately kept — an older DocumentVersion
            // row still points at it, and version history has to remain
            // retrievable.
            logger.LogInformation(
                "Added version {Version} to document {DocumentId}", updated.Version, id);

            // AddVersionAsync already reset the document to Pending, since the
            // stored chunks describe content that is no longer current.
            ingestion.Enqueue(id);

            return updated.ToViewModel();
        }
        catch
        {
            await storage.DeleteManyAsync([storagePath], ct);
            throw;
        }
    }

    public async Task<DocumentViewModel> UpdateAsync(
        Guid id,
        UpdateDocumentRequest request,
        CancellationToken ct = default)
    {
        if (request.Title is not null && string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("Title cannot be empty.");

        var update = new DocumentMetadataUpdateDto
        {
            Title = request.Title?.Trim(),
            Description = request.Description?.Trim(),
            Tags = request.Tags
                ?.Select(tag => tag.Trim().TrimStart('#').ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct()
                .ToList(),
            IsStarred = request.IsStarred,
        };

        var updated = await documents.UpdateMetadataAsync(id, update, ct)
            ?? throw new NotFoundException("Document", id);

        return updated.ToViewModel();
    }

    public async Task<DocumentViewModel> MoveAsync(
        Guid id,
        Guid folderId,
        CancellationToken ct = default)
    {
        if (!await folders.ExistsAsync(folderId, ct))
            throw new NotFoundException("Folder", folderId);

        var moved = await documents.MoveAsync(id, folderId, ct)
            ?? throw new NotFoundException("Document", id);

        return moved.ToViewModel();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var orphanedBlobs = await documents.DeleteAsync(id, ct);

        if (orphanedBlobs.Count == 0)
            throw new NotFoundException("Document", id);

        await storage.DeleteManyAsync(orphanedBlobs, ct);
        logger.LogInformation(
            "Deleted document {DocumentId} and {BlobCount} stored files", id, orphanedBlobs.Count);
    }

    public async Task<LibraryStatsViewModel> GetStatsAsync(CancellationToken ct = default) =>
        (await documents.GetStatsAsync(ct)).ToViewModel();

    public Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken ct = default) =>
        documents.GetAllTagsAsync(ct);

    public async Task<IReadOnlyList<UserViewModel>> GetOwnersAsync(CancellationToken ct = default)
    {
        var owners = await documents.GetOwnersAsync(ct);
        return [.. owners.Select(owner => owner.ToViewModel())];
    }

    /// <summary>
    /// Returns the sanitised file name and its extension, or throws with a
    /// message the user can act on.
    /// </summary>
    private static (string FileName, string Extension) ValidateUpload(UploadRequest request)
    {
        if (request.SizeBytes <= 0)
            throw new ValidationException("The uploaded file is empty.");

        if (request.SizeBytes > MaxUploadBytes)
            throw new ValidationException(
                $"Files cannot exceed {MaxUploadBytes / 1024 / 1024} MB.");

        // Strip any directory component a client may have sent; only the leaf
        // name is ever stored or echoed back.
        var fileName = Path.GetFileName(request.FileName?.Trim() ?? string.Empty);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ValidationException("A file name is required.");

        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

        if (string.IsNullOrEmpty(extension))
            throw new ValidationException("The file must have an extension.");

        if (BlockedExtensions.Contains(extension))
            throw new ValidationException($".{extension} files are not allowed.");

        return (fileName, extension);
    }
}
