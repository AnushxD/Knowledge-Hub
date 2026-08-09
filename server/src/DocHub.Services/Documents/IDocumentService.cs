using DocHub.Services.ViewModels;

namespace DocHub.Services.Documents;

/// <summary>
/// Reads the mirrored library, and edits the metadata the hub owns.
///
/// There is no upload, move or delete: a document exists because a file exists
/// in the repository, and the only thing that changes that is a sync. What can
/// be edited here is what GitLab has no opinion about — title, description,
/// tags and starring.
/// </summary>
public interface IDocumentService
{
    Task<IReadOnlyList<DocumentViewModel>> QueryAsync(
        DocumentQueryRequest request,
        CancellationToken ct = default);

    Task<DocumentDetailViewModel> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Streams the file straight from the repository. Nothing is cached: the
    /// hub keeps no copy of the bytes, so what a reader downloads is what the
    /// branch holds right now, not what the last sync happened to see.
    /// </summary>
    Task<DocumentContent> DownloadAsync(Guid id, CancellationToken ct = default);

    Task<DocumentViewModel> UpdateAsync(
        Guid id,
        UpdateDocumentRequest request,
        CancellationToken ct = default);

    Task<LibraryStatsViewModel> GetStatsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken ct = default);
}
