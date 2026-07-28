using DocHub.Services.ViewModels;

namespace DocHub.Services.Ingestion;

/// <summary>
/// Extracts, chunks and embeds an uploaded document so it becomes searchable.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Runs the whole pipeline for one document and records the outcome on it.
    ///
    /// Invoked by the background worker, never inline on a request — embedding
    /// a large document takes far longer than an upload should. Safe to run
    /// twice: chunks are replaced wholesale rather than appended.
    /// </summary>
    Task IngestAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Puts a document back in the queue, clearing any previous failure. Backs
    /// the retry action on a failed document.
    /// </summary>
    Task<DocumentViewModel> RequeueAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// File extensions the pipeline can currently index. Shown in the UI so a
    /// user knows before uploading whether a file will become searchable.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }
}

/// <summary>
/// Hands a document to the background worker.
///
/// The Service layer only ever sees this, so Hangfire stays a host concern —
/// and a test can assert that an upload was queued without running a job
/// server.
/// </summary>
public interface IIngestionQueue
{
    /// <summary>
    /// Queues a document for ingestion. Returns immediately; failures inside
    /// the job are recorded on the document, not thrown back to the caller.
    /// </summary>
    void Enqueue(Guid documentId);
}
