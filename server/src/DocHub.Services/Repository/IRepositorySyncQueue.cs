namespace DocHub.Services.Repository;

/// <summary>
/// Hands a sync to the background worker.
///
/// The Service layer only ever sees this, so Hangfire stays a host concern —
/// and a test can assert that a push webhook queued a sync without running a
/// job server.
///
/// Queued rather than awaited for the same reason ingestion is: mirroring a
/// repository of any size takes minutes, and GitLab hangs up on a webhook that
/// does not answer promptly.
/// </summary>
public interface IRepositorySyncQueue
{
    /// <param name="actorId">
    /// Who asked, for the activity trail. Null for a webhook, where nobody did.
    /// </param>
    void Enqueue(Guid? actorId);
}
