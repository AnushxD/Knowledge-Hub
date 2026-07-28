namespace DocHub.Integrations.Storage;

/// <summary>
/// A file retrieved from storage. Dispose it to release the underlying network
/// stream — callers normally stream it straight to the HTTP response.
/// </summary>
public sealed record StoredFile(
    Stream Content,
    string ContentType,
    long SizeBytes) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// Blob storage for uploaded document files — Azurite locally, Azure Blob
/// Storage in production, with the same implementation behind both.
///
/// The Service layer only ever sees this interface, so swapping storage (or
/// stubbing it in a test) never touches business logic.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Makes sure the backing store is usable — creating the container if it is
    /// missing. Called once at startup so the first upload is not paying for
    /// setup, and so a misconfigured account is obvious immediately rather than
    /// when a user first tries to upload. Safe to call repeatedly.
    /// </summary>
    Task EnsureReadyAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores a file and returns the storage path to persist against the
    /// document row.
    ///
    /// The path is generated server-side from <paramref name="originalFileName"/>'s
    /// extension only — the supplied name is never used to build the path, so a
    /// crafted filename cannot escape the container or overwrite another blob.
    /// </summary>
    Task<string> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken ct = default);

    /// <summary>Opens a stored file, or returns null when the path no longer exists.</summary>
    Task<StoredFile?> OpenReadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Deletes one blob. Returns false when it was already gone.</summary>
    Task<bool> DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes many blobs, used when a document or a whole folder is removed.
    /// Never throws for paths that no longer exist — deletion is best-effort
    /// cleanup and must not fail the user's request.
    /// </summary>
    Task DeleteManyAsync(IEnumerable<string> storagePaths, CancellationToken ct = default);

    Task<bool> ExistsAsync(string storagePath, CancellationToken ct = default);
}
