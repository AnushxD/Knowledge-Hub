namespace DocHub.DataAccess.Entities;

/// <summary>
/// One historical revision of a document. Every upload against an existing
/// document writes a new blob and a new row here, so previous files stay
/// retrievable rather than being overwritten.
/// </summary>
public class DocumentVersion
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public int VersionNumber { get; set; }

    /// <summary>Blob path of this specific revision.</summary>
    public required string StoragePath { get; set; }

    public long SizeBytes { get; set; }

    public string? Note { get; set; }

    public Guid ChangedById { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public Document? Document { get; set; }

    public User? ChangedBy { get; set; }
}
