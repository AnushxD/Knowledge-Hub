using System.Diagnostics;
using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Embeddings;
using DocHub.Integrations.Storage;
using DocHub.Services.Ingestion.Extraction;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Ingestion;

internal sealed class IngestionService(
    IDocumentRepository documents,
    IChunkRepository chunks,
    IFileStorage storage,
    ITextExtractorRegistry extractors,
    ITextChunker chunker,
    IEmbeddingProvider embeddings,
    IIngestionQueue queue,
    ILogger<IngestionService> logger) : IIngestionService
{
    public IReadOnlyList<string> SupportedExtensions => extractors.SupportedExtensions;

    public async Task IngestAsync(Guid documentId, CancellationToken ct = default)
    {
        var detail = await documents.GetByIdAsync(documentId, ct);

        if (detail is null)
        {
            // Deleted between being queued and being picked up. Nothing to do,
            // and nothing wrong — do not fail the job over it.
            logger.LogInformation(
                "Skipping ingestion for {DocumentId}: the document no longer exists.", documentId);
            return;
        }

        var document = detail.Document;
        var extractor = extractors.Find(document.Extension);

        if (extractor is null)
        {
            await FailAsync(documentId,
                $".{document.Extension} files cannot be indexed yet. Supported types: "
                + string.Join(", ", extractors.SupportedExtensions.Select(e => "." + e)),
                ct);
            return;
        }

        await documents.SetStatusAsync(documentId, IngestionStatus.Indexing, ct: ct);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var text = await ExtractAsync(extractor, detail.StoragePath, document.Extension, ct);

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
                document.Version,
                [.. chunked.Select((chunk, index) => new NewChunkDto(
                    chunk.Ordinal, chunk.Text, chunk.SectionRef, chunk.TokenCount, vectors[index]))],
                ct);

            await documents.SetStatusAsync(
                documentId, IngestionStatus.Indexed, chunkCount: chunked.Count, ct: ct);

            logger.LogInformation(
                "Indexed document {DocumentId} ({FileName}): {ChunkCount} chunks in {ElapsedMs}ms "
                + "using {Provider}",
                documentId, document.FileName, chunked.Count,
                stopwatch.ElapsedMilliseconds, embeddings.Name);
        }
        catch (TextExtractionException exception)
        {
            // The file itself is the problem; retrying will fail identically.
            logger.LogWarning(exception,
                "Extraction failed permanently for document {DocumentId}", documentId);

            await FailAsync(documentId, exception.Message, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Transient by assumption — the model was down, Postgres blipped.
            // The failure is recorded so the user is not left staring at a
            // document stuck on "indexing", and then rethrown so the worker's
            // retry policy gets its turn. A later attempt that succeeds
            // overwrites this status with Indexed.
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

        return reset.ToViewModel();
    }

    private async Task<ExtractedText> ExtractAsync(
        ITextExtractor extractor,
        string storagePath,
        string extension,
        CancellationToken ct)
    {
        await using var file = await storage.OpenReadAsync(storagePath, ct)
            ?? throw new TextExtractionException(
                $"The stored file '{storagePath}' is missing, so there is nothing to index.");

        return await extractor.ExtractAsync(file.Content, extension, ct);
    }

    private async Task FailAsync(Guid documentId, string reason, CancellationToken ct) =>
        await documents.SetStatusAsync(documentId, IngestionStatus.Failed, reason, ct: ct);

    /// <summary>
    /// A one-line reason fit to show a user. The full exception is already in
    /// the log; the document row should not carry a stack trace.
    /// </summary>
    private static string Summarise(Exception exception) => exception switch
    {
        EmbeddingException => $"Embedding failed: {exception.Message}",
        _ => $"Ingestion failed: {exception.Message}",
    };
}
