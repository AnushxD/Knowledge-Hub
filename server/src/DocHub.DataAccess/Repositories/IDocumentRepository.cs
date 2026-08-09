using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;

namespace DocHub.DataAccess.Repositories;

/// <summary>Persistence for the documents mirrored from the repository.</summary>
public interface IDocumentRepository
{
    Task<IReadOnlyList<DocumentDto>> QueryAsync(DocumentQueryDto query, CancellationToken ct = default);

    Task<DocumentDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Path within the repository, or null when the document is unknown. What
    /// the download proxy needs and nothing more.
    /// </summary>
    Task<string?> GetRepositoryPathAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Every mirrored file, as the minimum sync needs to diff the tree. A whole
    /// <see cref="DocumentDto"/> each would pull the description, tags and
    /// timestamps of a repository's worth of files to compare two strings.
    /// </summary>
    Task<IReadOnlyList<MirroredFileDto>> GetMirrorAsync(CancellationToken ct = default);

    Task<DocumentDto> CreateAsync(NewDocumentDto document, CancellationToken ct = default);

    /// <summary>
    /// Repoints a document at new repository content and drops it back to
    /// Pending — the stored chunks describe a revision the repository no longer
    /// has, and leaving them searchable would let the assistant cite text that
    /// is not in the file any more.
    /// </summary>
    Task<DocumentDto?> SetContentAsync(
        Guid id,
        string blobSha,
        string? commitSha,
        CancellationToken ct = default);

    /// <summary>
    /// Records that sync saw these files unchanged, so "last synced" means what
    /// it says on a document nothing has happened to.
    /// </summary>
    Task TouchAsync(IReadOnlyList<Guid> ids, DateTimeOffset at, CancellationToken ct = default);

    Task<DocumentDto?> UpdateMetadataAsync(
        Guid id,
        DocumentMetadataUpdateDto update,
        CancellationToken ct = default);

    /// <param name="sizeBytes">
    /// Set once ingestion has the bytes in hand. Null leaves the recorded size
    /// alone, which is what every call but that one wants.
    /// </param>
    Task<DocumentDto?> SetStatusAsync(
        Guid id,
        IngestionStatus status,
        string? failureReason = null,
        int? chunkCount = null,
        long? sizeBytes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Removes documents whose files have left the repository, and their chunks
    /// with them. Returns how many rows went.
    /// </summary>
    Task<int> DeleteManyAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    Task<LibraryStatsDto> GetStatsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct = default);
}
