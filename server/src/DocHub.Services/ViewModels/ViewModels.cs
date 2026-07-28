namespace DocHub.Services.ViewModels;

/// <summary>
/// Contracts exchanged with the outside world. Controllers accept and return
/// these; the Service layer converts them to DTOs before touching Data Access,
/// so a storage change never reshapes the public API.
/// </summary>
public record FolderViewModel(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Path,
    int DocumentCount);

public record UserViewModel(Guid Id, string Name, string Email, string Initials);

public record DocumentViewModel(
    Guid Id,
    Guid FolderId,
    string Title,
    string? Description,
    string FileName,
    string Extension,
    long SizeBytes,
    int Version,
    IReadOnlyList<string> Tags,
    UserViewModel Owner,
    string Status,
    string? FailureReason,
    int? ChunkCount,
    bool IsStarred,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record DocumentVersionViewModel(
    int Version,
    long SizeBytes,
    string? Note,
    UserViewModel ChangedBy,
    DateTimeOffset ChangedAt);

public record DocumentDetailViewModel(
    DocumentViewModel Document,
    IReadOnlyList<FolderViewModel> Breadcrumb,
    IReadOnlyList<DocumentVersionViewModel> Versions,
    /// <summary>
    /// Extracted, embedded chunks. Always empty until the phase 2 ingestion
    /// pipeline exists — the field is here so the citation contract the client
    /// already implements does not change shape later.
    /// </summary>
    IReadOnlyList<DocumentSectionViewModel> Sections);

public record DocumentSectionViewModel(int ChunkId, string Heading, int Page, string Body);

public record LibraryStatsViewModel(
    int Documents,
    int Indexed,
    int InPipeline,
    int Failed,
    int Folders,
    long StorageBytes,
    int Chunks);

// ---- requests ---------------------------------------------------------------

public record CreateFolderRequest(Guid? ParentId, string Name);

public record RenameFolderRequest(string Name);

public record UpdateDocumentRequest(
    string? Title,
    string? Description,
    IReadOnlyList<string>? Tags,
    bool? IsStarred);

public record MoveDocumentRequest(Guid FolderId);

/// <summary>
/// An uploaded file, decoupled from ASP.NET Core. The Service layer takes a
/// plain stream so it never depends on IFormFile — that keeps services testable
/// without spinning up the web stack.
/// </summary>
public record UploadRequest(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Note = null);

/// <summary>Filter for a document listing, as it arrives from the query string.</summary>
public record DocumentQueryRequest
{
    public Guid? FolderId { get; init; }
    public bool Recursive { get; init; } = true;
    public string? Text { get; init; }
    public string[]? Status { get; init; }
    public string[]? Extension { get; init; }
    public string[]? Tag { get; init; }
    public Guid? OwnerId { get; init; }
    public bool StarredOnly { get; init; }
    public string? Sort { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 200;
}

/// <summary>A file being streamed back to the caller.</summary>
public record DocumentContent(Stream Content, string ContentType, string FileName, long SizeBytes);
