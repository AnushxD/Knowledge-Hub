using DocHub.Services.Ingestion;
using Hangfire;

namespace DocHub.Api.Infrastructure;

/// <summary>
/// Hands ingestion work to Hangfire.
///
/// Lives in the host rather than in the Service layer so the choice of job
/// runner stays a composition decision: moving to a queue-backed worker later
/// replaces this class and nothing else.
/// </summary>
internal sealed class HangfireIngestionQueue(IBackgroundJobClient jobs) : IIngestionQueue
{
    public void Enqueue(Guid documentId) =>
        // Hangfire resolves IIngestionService from a fresh scope when the job
        // runs, so the job outlives the request that queued it.
        jobs.Enqueue<IIngestionService>(
            service => service.IngestAsync(documentId, CancellationToken.None));
}
