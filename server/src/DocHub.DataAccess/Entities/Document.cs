namespace DocHub.DataAccess.Entities;

/// <summary>
/// An uploaded document. The file itself lives in blob storage; this row holds
/// the metadata and the pointer to it.
/// </summary>
public class Document
{
    public Guid Id { get; set; }

    public Guid FolderId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public required string FileName { get; set; }

    /// <summary>Lower-case, no leading dot ("pdf").</summary>
    public required string Extension { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Blob path of the current version.</summary>
    public required string StoragePath { get; set; }

    /// <summary>Current version number; starts at 1 and increments per upload.</summary>
    public int Version { get; set; } = 1;

    public string[] Tags { get; set; } = [];

    public Guid OwnerId { get; set; }

    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;

    /// <summary>Populated only when <see cref="Status"/> is Failed.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Number of rows in <see cref="Chunks"/>; null until ingestion succeeds.</summary>
    public int? ChunkCount { get; set; }

    public bool IsStarred { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Folder? Folder { get; set; }

    public User? Owner { get; set; }

    public ICollection<DocumentVersion> Versions { get; set; } = [];

    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}
