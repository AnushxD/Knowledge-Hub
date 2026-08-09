using DocHub.Services.Repository;
using Hangfire;

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
    public void Enqueue(Guid? actorId) =>
        // Hangfire resolves the service from a fresh scope when the job runs,
        // so the sync outlives the request that queued it — which matters more
        // here than for ingestion, since a full mirror can run for minutes.
        jobs.Enqueue<IRepositoryMirrorService>(
            service => service.SyncAsync(actorId, CancellationToken.None));
}
