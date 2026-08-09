namespace DocHub.DataAccess.Entities;

/// <summary>What happened. Persisted as text, so a dump stays readable.</summary>
public enum ActivityType
{
    /// <summary>A file appeared in the repository and was mirrored.</summary>
    Added,

    /// <summary>A file's contents changed in the repository.</summary>
    Changed,

    /// <summary>Hub-local metadata was edited — title, description or tags.</summary>
    Updated,

    /// <summary>A file left the repository and was removed from the mirror.</summary>
    Removed,

    Indexed,

    Failed,

    /// <summary>A whole sync finished, successfully or not.</summary>
    Synced,
}

/// <summary>
/// One thing that happened, for the activity feed.
///
/// Append-only: rows are written and read, never edited. That is what makes it
/// worth anything — an audit trail you can revise is a record of what someone
/// last decided it should say.
///
/// <see cref="Target"/> denormalises the name at the time, exactly as citations
/// do. A file leaving the repository must not blank out the record of it having
/// left, which is the one entry most likely to be asked about.
/// </summary>
public class ActivityEvent
{
    public Guid Id { get; set; }

    public ActivityType Type { get; set; }

    /// <summary>
    /// Who did it, or null when nobody did.
    ///
    /// Nullable on purpose. Most of this feed is now the repository changing
    /// under a webhook, with no one signed in — attributing that to the seeded
    /// administrator would put a name against work that account did not do, and
    /// inventing a "system" user would put a row in the user table that cannot
    /// sign in. An absent actor renders as the sync itself.
    /// </summary>
    public Guid? ActorId { get; set; }

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
