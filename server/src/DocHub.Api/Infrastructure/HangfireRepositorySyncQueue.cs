using DocHub.Services.Repository;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace DocHub.Api.Infrastructure;

/// <summary>
/// Hands a repository sync to Hangfire.
///
/// Lives in the host rather than in the Service layer so the choice of job
/// runner stays a composition decision, exactly as it does for ingestion.
/// </summary>
internal sealed class HangfireRepositorySyncQueue(IBackgroundJobClient jobs)
    : IRepositorySyncQueue
{
    /// <summary>
    /// Syncs run on their own queue, drained ahead of ingestion.
    ///
    /// Not a nicety. The first sync of a real repository queues hundreds of
    /// ingestion jobs, and on a shared queue with two workers the next sync
    /// lands behind all of them — measured at 636 documents, a "Sync now" sat
    /// unstarted while the backlog drained, and the screen showed the previous
    /// run's numbers as if nothing had been asked for. Reading a tree is
    /// seconds of work and it decides what everything else does; it does not
    /// belong behind the work it caused.
    /// </summary>
    public const string QueueName = "sync";

    public void Enqueue(Guid? actorId) =>
        // Hangfire resolves the service from a fresh scope when the job runs,
        // so the sync outlives the request that queued it — which matters more
        // here than for ingestion, since a full mirror can run for minutes.
        jobs.Create(
            Job.FromExpression<IRepositoryMirrorService>(
                service => service.SyncAsync(actorId, CancellationToken.None)),
            new EnqueuedState(QueueName));
}
