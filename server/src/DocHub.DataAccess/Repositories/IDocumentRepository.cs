using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;

namespace DocHub.DataAccess.Repositories;

/// <summary>Persistence for documents and their version history.</summary>
public interface IDocumentRepository
{
    Task<IReadOnlyList<DocumentDto>> QueryAsync(DocumentQueryDto query, CancellationToken ct = default);

    Task<DocumentDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Blob path of the current version, or null when the document is unknown.</summary>
    Task<string?> GetStoragePathAsync(Guid id, CancellationToken ct = default);

    Task<DocumentDto> CreateAsync(NewDocumentDto document, CancellationToken ct = default);

    /// <summary>Records a new revision: bumps the version and repoints the blob.</summary>
    Task<DocumentDto?> AddVersionAsync(
        Guid id,
        string storagePath,
        long sizeBytes,
        string? note,
        Guid changedById,
        CancellationToken ct = default);

    Task<DocumentDto?> UpdateMetadataAsync(
        Guid id,
        DocumentMetadataUpdateDto update,
        CancellationToken ct = default);

    Task<DocumentDto?> SetStatusAsync(
        Guid id,
        IngestionStatus status,
        string? failureReason = null,
        int? chunkCount = null,
        CancellationToken ct = default);

    Task<DocumentDto?> MoveAsync(Guid id, Guid folderId, CancellationToken ct = default);

    /// <summary>Deletes the document. Returns every blob path it owned, so the caller can free them.</summary>
    Task<IReadOnlyList<string>> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<LibraryStatsDto> GetStatsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct = default);
}
