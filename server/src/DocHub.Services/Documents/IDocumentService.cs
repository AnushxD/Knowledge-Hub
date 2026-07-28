using DocHub.Services.ViewModels;

namespace DocHub.Services.Documents;

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentViewModel>> QueryAsync(
        DocumentQueryRequest request,
        CancellationToken ct = default);

    Task<DocumentDetailViewModel> GetAsync(Guid id, CancellationToken ct = default);

    Task<DocumentContent> DownloadAsync(Guid id, CancellationToken ct = default);

    Task<DocumentViewModel> UploadAsync(
        Guid folderId,
        UploadRequest request,
        CancellationToken ct = default);

    /// <summary>Uploads a replacement file, creating a new version of an existing document.</summary>
    Task<DocumentViewModel> AddVersionAsync(
        Guid id,
        UploadRequest request,
        CancellationToken ct = default);

    Task<DocumentViewModel> UpdateAsync(
        Guid id,
        UpdateDocumentRequest request,
        CancellationToken ct = default);

    Task<DocumentViewModel> MoveAsync(Guid id, Guid folderId, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<LibraryStatsViewModel> GetStatsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken ct = default);
}
