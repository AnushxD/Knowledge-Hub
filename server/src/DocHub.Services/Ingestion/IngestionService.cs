using System.Diagnostics;
using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Embeddings;
using DocHub.Integrations.SourceControl;
using DocHub.Services.Activity;
using DocHub.Services.Ingestion.Extraction;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Ingestion;

internal sealed class IngestionService(
    IDocumentRepository documents,
    IChunkRepository chunks,
    ISourceRepositoryClient repository,
    ITextExtractorRegistry extractors,
    ITextChunker chunker,
    IEmbeddingProvider embeddings,
    IIngestionQueue queue,
    IActivityLog activity,
    ILogger<IngestionService> logger) : IIngestionService
{
    public IReadOnlyList<string> SupportedExtensions => extractors.SupportedExtensions;

    public async Task IngestAsync(Guid documentId, CancellationToken ct = default)
    {
        var detail = await documents.GetByIdAsync(documentId, ct);

        if (detail is null)
        {
            // Removed by a sync between being queued and being picked up.
            // Nothing to do, and nothing wrong — do not fail the job over it.
            logger.LogInformation(
                "Skipping ingestion for {DocumentId}: the document no longer exists.", documentId);
            return;
        }

        var document = detail.Document;
        var extractor = extractors.Find(document.Extension);

        if (extractor is null)
        {
            // Sync filters these out, so reaching here means the extractor set
            // shrank under a document that was already mirrored.
            await FailAsync(documentId,
                $".{document.Extension} files cannot be indexed. Supported types: "
                + string.Join(", ", extractors.SupportedExtensions.Select(e => "." + e)),
                ct);
            return;
        }

        await documents.SetStatusAsync(documentId, IngestionStatus.Indexing, ct: ct);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var (text, sizeBytes) = await ExtractAsync(
                extractor, document.RepositoryPath, document.Extension, ct);

            if (text.IsEmpty)
            {
                await FailAsync(documentId,
                    "No text could be extracted. Scanned images and empty files are not "
                    + "searchable until OCR is added.",
                    ct);
                return;
            }

            var chunked = chunker.Chunk(text);

            if (chunked.Count == 0)
            {
                await FailAsync(documentId, "The document produced no indexable content.", ct);
                return;
            }

            var vectors = await embeddings.EmbedDocumentsAsync(
                [.. chunked.Select(chunk => chunk.Text)], ct);

            if (vectors.Count != chunked.Count)
            {
                throw new EmbeddingException(
                    $"Provider '{embeddings.Name}' returned {vectors.Count} vectors for "
                    + $"{chunked.Count} chunks.");
            }

            await chunks.ReplaceAsync(
                documentId,
                document.BlobSha,
                [.. chunked.Select((chunk, index) => new NewChunkDto(
                    chunk.Ordinal, chunk.Text, chunk.SectionRef, chunk.TokenCount, vectors[index]))],
                ct);

            await documents.SetStatusAsync(
                documentId,
                IngestionStatus.Indexed,
                chunkCount: chunked.Count,
                sizeBytes: sizeBytes,
                ct: ct);

            // No actor: the file came from the repository and the job runs on a
            // background worker where nobody is signed in.
            await activity.RecordForAsync(
                actorId: null, ActivityType.Indexed, document.Title, documentId, ct);

            logger.LogInformation(
                "Indexed document {DocumentId} ({Path}): {ChunkCount} chunks in {ElapsedMs}ms "
                + "using {Provider}",
                documentId, document.RepositoryPath, chunked.Count,
                stopwatch.ElapsedMilliseconds, embeddings.Name);
        }
        catch (Exception exception)
            when (exception is TextExtractionException or FileTooLargeException)
        {
            // The file itself is the problem; retrying will fail identically
            // until the repository holds a different revision of it — which
            // arrives as a sync, and a sync requeues.
            logger.LogWarning(exception,
                "Extraction failed permanently for document {DocumentId}", documentId);

            await FailAsync(documentId, exception.Message, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Transient by assumption — GitLab was unreachable, the model was
            // down, Postgres blipped. The failure is recorded so the user is not
            // left staring at a document stuck on "indexing", and then rethrown
            // so the worker's retry policy gets its turn. A later attempt that
            // succeeds overwrites this status with Indexed.
            logger.LogError(exception,
                "Ingestion failed for document {DocumentId}; leaving it for retry", documentId);

            await FailAsync(documentId, Summarise(exception), ct);
            throw;
        }
    }

    public async Task<DocumentViewModel> RequeueAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        var reset = await documents.SetStatusAsync(documentId, IngestionStatus.Pending, ct: ct)
            ?? throw new NotFoundException("Document", documentId);

        queue.Enqueue(documentId);
        logger.LogInformation("Requeued document {DocumentId} for ingestion", documentId);

        return reset.ToViewModel(repository.WebUrlFor(reset.RepositoryPath));
    }

    /// <returns>
    /// The extracted text and the size of the file it came from. The size is
    /// reported by the repository rather than measured here — the extractors
    /// consume the stream, and a file's length is not knowable afterwards
    /// without buffering the whole thing to find out.
    /// </returns>
    private async Task<(ExtractedText Text, long SizeBytes)> ExtractAsync(
        ITextExtractor extractor,
        string repositoryPath,
        string extension,
        CancellationToken ct)
    {
        await using var file = await repository.OpenFileAsync(repositoryPath, ct)
            ?? throw new TextExtractionException(
                $"'{repositoryPath}' is no longer in the repository, so there is nothing to "
                + "index. The next sync will remove it.");

        var text = await extractor.ExtractAsync(file.Content, extension, ct);
        return (text, file.SizeBytes ?? 0);
    }

    private async Task FailAsync(Guid documentId, string reason, CancellationToken ct)
    {
        await documents.SetStatusAsync(documentId, IngestionStatus.Failed, reason, ct: ct);

        // A failed document is invisible to search and the assistant, so the
        // feed is where anyone is most likely to notice it at all.
        var failed = await documents.GetByIdAsync(documentId, ct);

        if (failed is not null)
        {
            await activity.RecordForAsync(
                actorId: null, ActivityType.Failed, failed.Document.Title, documentId, ct);
        }
    }

    /// <summary>
    /// A one-line reason fit to show a user. The full exception is already in
    /// the log; the document row should not carry a stack trace.
    /// </summary>
    private static string Summarise(Exception exception) => exception switch
    {
        EmbeddingException => $"Embedding failed: {exception.Message}",
        SourceRepositoryException => $"Could not read the file: {exception.Message}",
        _ => $"Ingestion failed: {exception.Message}",
    };
}
