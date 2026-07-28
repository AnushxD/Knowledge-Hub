using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations.Storage;

internal sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobServiceClient _client;
    private readonly FileStorageOptions _options;
    private readonly ILogger<AzureBlobFileStorage> _logger;

    public AzureBlobFileStorage(
        BlobServiceClient client,
        IOptions<FileStorageOptions> options,
        ILogger<AzureBlobFileStorage> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The container is a provisioned resource, not something a request
    /// creates. The read and write paths below assume it already exists —
    /// <see cref="EnsureReadyAsync"/> is the only method that provisions it,
    /// and it is invoked from the explicit `init-storage` setup command.
    /// </summary>
    private BlobContainerClient Container =>
        _client.GetBlobContainerClient(_options.ContainerName);

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        // Private access explicitly: documents must never be world-readable,
        // and stating it here stops a future default change from publishing
        // them silently.
        var response = await Container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        _logger.LogInformation(
            response is null
                ? "Blob container {Container} already exists."
                : "Created blob container {Container}.",
            _options.ContainerName);
    }

    public async Task<string> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken ct = default)
    {
        var storagePath = BuildStoragePath(originalFileName);
        var blob = Container.GetBlobClient(storagePath);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            },
            ct);

        _logger.LogInformation(
            "Stored blob {StoragePath} ({ContentType})", storagePath, contentType);

        return storagePath;
    }

    public async Task<StoredFile?> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        var blob = Container.GetBlobClient(storagePath);

        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            var stream = await blob.OpenReadAsync(cancellationToken: ct);

            return new StoredFile(
                stream,
                properties.Value.ContentType ?? "application/octet-stream",
                properties.Value.ContentLength);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // A missing blob is a normal outcome (deleted, or a stale link),
            // not an exceptional one — the caller decides what to do about it.
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var response = await Container
            .GetBlobClient(storagePath)
            .DeleteIfExistsAsync(cancellationToken: ct);

        return response.Value;
    }

    public async Task DeleteManyAsync(
        IEnumerable<string> storagePaths,
        CancellationToken ct = default)
    {
        foreach (var storagePath in storagePaths.Distinct())
        {
            try
            {
                await DeleteAsync(storagePath, ct);
            }
            catch (RequestFailedException ex)
            {
                // Cleanup runs after the database row is already gone. Failing
                // here would surface an error for work the user considers done,
                // so log the orphan and carry on.
                _logger.LogWarning(
                    ex, "Could not delete blob {StoragePath}; it may be orphaned.", storagePath);
            }
        }
    }

    public async Task<bool> ExistsAsync(string storagePath, CancellationToken ct = default)
    {
        var response = await Container.GetBlobClient(storagePath).ExistsAsync(ct);
        return response.Value;
    }

    /// <summary>
    /// Date-partitioned, randomly named path: "2026/07/0198f3c2….pdf".
    ///
    /// The date prefix keeps any single virtual directory from growing
    /// unbounded and makes lifecycle rules straightforward later. Only the
    /// extension is taken from the user's filename, and it is whitelisted
    /// character-by-character so nothing from user input can alter the path.
    /// </summary>
    private static string BuildStoragePath(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        var safeExtension = string.IsNullOrWhiteSpace(extension) || extension.Length > 16
            ? string.Empty
            : new string([.. extension.Where(c => char.IsAsciiLetterOrDigit(c) || c == '.')]);

        var now = DateTimeOffset.UtcNow;
        return $"{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}{safeExtension.ToLowerInvariant()}";
    }
}
