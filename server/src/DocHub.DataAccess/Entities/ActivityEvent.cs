namespace DocHub.DataAccess.Entities;

/// <summary>What happened. Persisted as text, so a dump stays readable.</summary>
public enum ActivityType
{
    Uploaded,
    Updated,
    Moved,
    Deleted,
    Indexed,
    Failed,
    FolderCreated,
    FolderDeleted,
}

/// <summary>
/// One thing somebody did, for the activity feed.
///
/// Append-only: rows are written and read, never edited. That is what makes it
/// worth anything — an audit trail you can revise is a record of what someone
/// last decided it should say.
///
/// <see cref="Target"/> denormalises the name at the time, exactly as citations
/// do. Deleting a document must not blank out the record of it having been
/// deleted, which is the one entry most likely to be asked about.
/// </summary>
public class ActivityEvent
{
    public Guid Id { get; set; }

    public ActivityType Type { get; set; }

    /// <summary>Who did it. Ingestion runs unattended, so it records the owner.</summary>
    public Guid ActorId { get; set; }

    public User? Actor { get; set; }

    /// <summary>The document or folder name as it was, not as it is now.</summary>
    public required string Target { get; set; }

    /// <summary>
    /// The document, when there is one, so the feed can link to it. No foreign
    /// key: the row has to outlive what it points at, and the client checks
    /// whether the document still exists by following the link.
    /// </summary>
    public Guid? TargetId { get; set; }

    public DateTimeOffset At { get; set; }
}
