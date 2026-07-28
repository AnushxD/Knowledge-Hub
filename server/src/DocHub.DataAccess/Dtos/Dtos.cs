using DocHub.DataAccess.Entities;

namespace DocHub.DataAccess.Dtos;

/// <summary>
/// Data Access exchanges DTOs with the Service layer — entities never leave
/// this project, so a schema change cannot ripple straight into Services or
/// out through the API.
/// </summary>
public record UserDto(Guid Id, string Name, string Email, string Role);

public record FolderDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Path,
    Guid OwnerId,
    int DocumentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record DocumentDto(
    Guid Id,
    Guid FolderId,
    string Title,
    string? Description,
    string FileName,
    string Extension,
    string ContentType,
    long SizeBytes,
    int Version,
    IReadOnlyList<string> Tags,
    UserDto Owner,
    IngestionStatus Status,
    string? FailureReason,
    int? ChunkCount,
    bool IsStarred,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record DocumentVersionDto(
    int VersionNumber,
    string StoragePath,
    long SizeBytes,
    string? Note,
    UserDto ChangedBy,
    DateTimeOffset ChangedAt);

/// <summary>A document plus everything the detail screen needs in one round trip.</summary>
public record DocumentDetailDto(
    DocumentDto Document,
    string StoragePath,
    IReadOnlyList<FolderDto> Breadcrumb,
    IReadOnlyList<DocumentVersionDto> Versions);

public record LibraryStatsDto(
    int Documents,
    int Indexed,
    int InPipeline,
    int Failed,
    int Folders,
    long StorageBytes,
    int Chunks);

/// <summary>Filter for a document listing. Null members mean "no constraint".</summary>
public record DocumentQueryDto
{
    public Guid? FolderId { get; init; }

    /// <summary>Include documents in descendant folders too.</summary>
    public bool Recursive { get; init; } = true;

    public string? Text { get; init; }

    public IReadOnlyList<IngestionStatus>? Statuses { get; init; }

    public IReadOnlyList<string>? Extensions { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public Guid? OwnerId { get; init; }

    public bool StarredOnly { get; init; }

    public DocumentSort Sort { get; init; } = DocumentSort.UpdatedDescending;

    public int Skip { get; init; }

    public int Take { get; init; } = 200;
}

public enum DocumentSort
{
    UpdatedDescending,
    UpdatedAscending,
    NameAscending,
    NameDescending,
    SizeDescending,
}

/// <summary>Everything needed to persist a freshly uploaded file.</summary>
public record NewDocumentDto
{
    public required Guid FolderId { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required string StoragePath { get; init; }
    public required Guid OwnerId { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Editable metadata. Null members are left unchanged.</summary>
public record DocumentMetadataUpdateDto
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool? IsStarred { get; init; }
}

// ---- chat ------------------------------------------------------------------

public record ChatSessionDto(
    Guid Id,
    string Title,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ChatMessageDto(
    Guid Id,
    Guid SessionId,
    ChatRole Role,
    string Content,
    IReadOnlyList<Citation> Citations,
    bool IsRefusal,
    DateTimeOffset CreatedAt);

/// <summary>A session together with its whole transcript.</summary>
public record ChatTranscriptDto(
    ChatSessionDto Session,
    IReadOnlyList<ChatMessageDto> Messages);

/// <summary>A message about to be appended to a session.</summary>
public record NewChatMessageDto
{
    public required ChatRole Role { get; init; }
    public required string Content { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];
    public bool IsRefusal { get; init; }
}

/// <summary>One chunk ready to be persisted, as produced by the ingestion pipeline.</summary>
public record NewChunkDto(
    int Ordinal,
    string Text,
    string? SectionRef,
    int TokenCount,
    float[] Embedding);

/// <summary>
/// A chunk that matched a search, carrying enough document context for a
/// result card and a citation without a second query per hit.
/// </summary>
/// <param name="Score">
/// Raw relevance from the branch that produced this hit — ts_rank for keyword,
/// cosine similarity for vector. The two are not comparable, which is exactly
/// why the service fuses on rank position instead of on this number.
/// </param>
public record ChunkMatchDto(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string FileName,
    string Extension,
    Guid FolderId,
    string FolderPath,
    int Ordinal,
    string? SectionRef,
    string Text,
    double Score);

/// <summary>
/// Filter for a chunk-level search. Only chunks of Indexed documents are ever
/// returned, so a document still in the pipeline (or one that failed) cannot be
/// surfaced or cited.
/// </summary>
public record ChunkSearchDto
{
    public required string Text { get; init; }

    /// <summary>Restricts to a folder and, since folders nest, its whole subtree.</summary>
    public Guid? FolderId { get; init; }

    public IReadOnlyList<string>? Extensions { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public Guid? OwnerId { get; init; }

    /// <summary>Candidates to pull from each branch before fusion.</summary>
    public int Limit { get; init; } = 40;
}
