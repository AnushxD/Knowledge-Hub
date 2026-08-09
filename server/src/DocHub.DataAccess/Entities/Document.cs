namespace DocHub.DataAccess.Entities;

/// <summary>
/// One file in the mirrored repository. GitLab holds the bytes and the history;
/// this row holds the metadata the hub adds on top — ingestion state, hub-local
/// tags and description, and the pointer back to the path the file lives at.
///
/// There is no storage path and no version number. The file is fetched from
/// GitLab by <see cref="RepositoryPath"/> whenever it is previewed, downloaded
/// or indexed, and its history is the repository's commit log rather than a
/// table here.
/// </summary>
public class Document
{
    public Guid Id { get; set; }

    public Guid FolderId { get; set; }

    /// <summary>
    /// Path within the repository, relative to the configured sub-path
    /// ("guides/onboarding/setup.md"). This is the document's real identity:
    /// sync matches on it, so hub-local metadata survives a file's contents
    /// changing but not the file being renamed or moved, which reads as a
    /// delete and an add.
    /// </summary>
    public required string RepositoryPath { get; set; }

    /// <summary>
    /// Git object id of the file's blob. Sync compares this against the tree to
    /// decide what actually changed — a push that touches a thousand files but
    /// alters two must only re-embed the two.
    /// </summary>
    public required string BlobSha { get; set; }

    /// <summary>
    /// Repository commit the current content was synced from. Recorded for
    /// display and support ("which revision is indexed?"), never used to decide
    /// whether a file changed — <see cref="BlobSha"/> is the only honest answer
    /// to that, since a commit touching other files leaves this one identical.
    /// </summary>
    public string? CommitSha { get; set; }

    public required string Title { get; set; }

    /// <summary>Hub-local, editable. Nothing in the repository corresponds to it.</summary>
    public string? Description { get; set; }

    public required string FileName { get; set; }

    /// <summary>Lower-case, no leading dot ("pdf").</summary>
    public required string Extension { get; set; }

    public required string ContentType { get; set; }

    /// <summary>
    /// Zero until the file has been fetched at least once. The tree listing
    /// GitLab returns carries no size, and asking for one per file would be a
    /// round trip per file on every sync — so this is filled in by ingestion,
    /// which has the bytes in hand anyway.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>Hub-local, editable.</summary>
    public string[] Tags { get; set; } = [];

    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;

    /// <summary>Populated only when <see cref="Status"/> is Failed.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Number of rows in <see cref="Chunks"/>; null until ingestion succeeds.</summary>
    public int? ChunkCount { get; set; }

    /// <summary>Hub-local, editable.</summary>
    public bool IsStarred { get; set; }

    /// <summary>When sync last saw this file in the repository tree.</summary>
    public DateTimeOffset LastSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Folder? Folder { get; set; }

    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}
