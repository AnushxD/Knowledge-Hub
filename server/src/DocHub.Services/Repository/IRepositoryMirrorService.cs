using DocHub.Services.ViewModels;

namespace DocHub.Services.Repository;

/// <summary>
/// Keeps the hub's document tree matching the repository's.
///
/// The repository is the system of record: nothing here writes to GitLab, and
/// nothing but this changes the mirror. A document exists because a file does.
/// </summary>
public interface IRepositoryMirrorService
{
    /// <summary>
    /// The repository being mirrored and the outcome of the last sync. Cheap —
    /// a single row read plus configuration, no call to GitLab — because it
    /// draws a screen and is polled while a sync runs.
    /// </summary>
    Task<RepositoryViewModel> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Reconciles the whole tree: lists the repository, adds what is new,
    /// re-ingests what changed, removes what has gone, and leaves untouched
    /// files untouched.
    ///
    /// Safe to run repeatedly — it compares blob ids, so a second run
    /// immediately after a first does nothing and costs one listing. Only one
    /// sync runs at a time; a second caller is told so rather than queued,
    /// since two syncs would reach the same answer twice.
    /// </summary>
    /// <param name="actorId">
    /// Who asked, for the activity trail. Null for a webhook, where nobody did.
    /// </param>
    Task<RepositoryViewModel> SyncAsync(Guid? actorId, CancellationToken ct = default);
}
