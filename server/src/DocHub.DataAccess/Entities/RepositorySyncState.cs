namespace DocHub.DataAccess.Entities;

/// <summary>How the last sync ended. Persisted as text, so a dump stays readable.</summary>
public enum SyncOutcome
{
    /// <summary>A sync is in flight. Written before the work, replaced after it.</summary>
    Running,

    Succeeded,

    /// <summary>
    /// The repository could not be read, or reading it threw part way. The
    /// documents already mirrored are left exactly as they were — a failed sync
    /// must not empty the library because GitLab was briefly unreachable.
    /// </summary>
    Failed,
}

/// <summary>
/// What the hub knows about the last time it mirrored the repository.
///
/// Keyed by project path and branch rather than by a singleton row: repointing
/// the configuration at a different repository or branch starts a fresh record
/// instead of quietly overwriting the old one with counts that describe
/// somewhere else.
/// </summary>
public class RepositorySyncState
{
    /// <summary>Namespaced project path, as GitLab spells it ("team/docs").</summary>
    public required string ProjectPath { get; set; }

    public required string Branch { get; set; }

    public SyncOutcome Outcome { get; set; }

    /// <summary>Head commit the mirror is current with. Null until a sync succeeds.</summary>
    public string? CommitSha { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while a sync is in flight.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Populated only when <see cref="Outcome"/> is Failed.</summary>
    public string? Error { get; set; }

    public int FilesAdded { get; set; }

    public int FilesUpdated { get; set; }

    public int FilesRemoved { get; set; }

    /// <summary>
    /// Files in the tree that no extractor can index. Counted rather than
    /// hidden: a repository is mostly source code, and "412 of 900 files are
    /// searchable" is the honest headline for a mirror of one.
    /// </summary>
    public int FilesSkipped { get; set; }

    /// <summary>
    /// Unchanged files whose document had never finished indexing, put back on
    /// the queue by this run.
    ///
    /// Separate from <see cref="FilesUpdated"/> because nothing about the
    /// repository changed: this is the mirror catching up with itself after a
    /// worker stopped part way through a backlog. Reported rather than done
    /// quietly — a run that says "0 added, 0 updated, 0 removed" while six
    /// hundred documents start indexing reads as a run that did nothing.
    /// </summary>
    public int FilesRequeued { get; set; }
}
