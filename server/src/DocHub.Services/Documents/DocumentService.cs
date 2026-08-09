using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.SourceControl;
using DocHub.Services.Activity;
using DocHub.Services.ViewModels;

namespace DocHub.Services.Documents;

internal sealed class DocumentService(
    IDocumentRepository documents,
    IChunkRepository chunks,
    IChatRepository chat,
    ISourceRepositoryClient repository,
    IActivityLog activity) : IDocumentService
{
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
            StarredOnly = request.StarredOnly,
            Sort = Mapping.ParseSort(request.Sort),
            Skip = Math.Max(0, request.Skip),
            // Clamped so a caller cannot ask for the entire library in one page.
            Take = Math.Clamp(request.Take, 1, 500),
        };

        var results = await documents.QueryAsync(query, ct);
        return [.. results.Select(ToViewModel)];
    }

    public async Task<DocumentDetailViewModel> GetAsync(Guid id, CancellationToken ct = default)
    {
        var detail = await documents.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Document", id);

        // Empty for anything that has not finished ingestion, which is also
        // exactly when the client shows the pipeline state instead of a preview.
        var sections = await chunks.GetForDocumentAsync(id, ct);

        // Sequential, not concurrent: both reads share the request-scoped
        // DbContext.
        var citedInAnswers = await chat.CountAnswersCitingAsync(id, ct);

        return detail.ToViewModel(
            repository.WebUrlFor(detail.Document.RepositoryPath), sections, citedInAnswers);
    }

    public async Task<DocumentContent> DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var detail = await documents.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Document", id);

        var document = detail.Document;

        var file = await repository.OpenFileAsync(document.RepositoryPath, ct)
            // The row exists but the file does not. Ordinary rather than
            // exceptional: the repository moved on and the hub has not synced
            // since, so the honest answer is that this document is gone.
            ?? throw new NotFoundException("Document", id);

        return new DocumentContent(
            file.Content,
            document.ContentType,
            document.FileName,
            file.SizeBytes ?? document.SizeBytes);
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

        // Starring is a personal bookmark, not an edit worth announcing. Left
        // in, every star and un-star would crowd genuine changes out of a feed
        // that only shows the most recent dozen.
        var starOnly = request.Title is null && request.Description is null && request.Tags is null;

        if (!starOnly)
            await activity.RecordAsync(ActivityType.Updated, updated.Title, updated.Id, ct: ct);

        return ToViewModel(updated);
    }

    public async Task<LibraryStatsViewModel> GetStatsAsync(CancellationToken ct = default) =>
        (await documents.GetStatsAsync(ct)).ToViewModel();

    public Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken ct = default) =>
        documents.GetAllTagsAsync(ct);

    private DocumentViewModel ToViewModel(DocumentDto document) =>
        document.ToViewModel(repository.WebUrlFor(document.RepositoryPath));
}
