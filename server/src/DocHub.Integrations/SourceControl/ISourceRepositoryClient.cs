namespace DocHub.Integrations.SourceControl;

/// <summary>One file in the repository tree.</summary>
/// <param name="Path">
/// Path relative to the configured sub-path, "/"-separated. This is what the
/// hub stores and what identifies the document.
/// </param>
/// <param name="Name">Leaf file name, including its extension.</param>
/// <param name="BlobSha">
/// Git object id of the contents. Two files with identical bytes share one, so
/// this identifies a revision of the content and not the file — which is
/// exactly what sync needs to answer "has this changed?".
/// </param>
public record RepositoryFile(string Path, string Name, string BlobSha);

/// <summary>An open file, streamed rather than buffered — some are large.</summary>
public sealed record RepositoryFileContent(
    Stream Content,
    long? SizeBytes) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await Content.DisposeAsync();
}

/// <summary>
/// The repository could not be read. Distinct from a file simply not being
/// there, which is a null return: this is the network, the token or the server.
/// </summary>
public sealed class SourceRepositoryException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Read access to the source repository the hub mirrors.
///
/// Deliberately read-only and deliberately narrow: listing the tree, opening a
/// file, and asking which commit is at the head is the whole of what mirroring
/// needs. Nothing here writes, so a token with <c>read_repository</c> is
/// sufficient and a bug in the hub cannot alter the team's documentation.
///
/// Provider-neutral by name so that pointing the hub at a different forge is
/// one more implementation and one registration branch, in keeping with the
/// same rule the LLM and embedding providers follow.
/// </summary>
public interface ISourceRepositoryClient
{
    /// <summary>Namespaced project path being mirrored, for display and for the sync record.</summary>
    string ProjectPath { get; }

    string Branch { get; }

    /// <summary>
    /// Where a human would go to read this file in GitLab. Used for the link
    /// out on the document screen — the hub shows the content, but the
    /// repository is where it is edited, and a reader who wants to change
    /// something has to be sent somewhere.
    /// </summary>
    Uri WebUrlFor(string repositoryPath);

    /// <summary>Commit at the head of the branch, or null if the branch is empty.</summary>
    Task<string?> GetHeadCommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Every file beneath the configured sub-path, recursively. Paginated
    /// internally; the whole tree comes back in one list because sync has to
    /// diff against the complete set to know what has been deleted.
    /// </summary>
    Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Opens a file's raw bytes, or returns null when the path is not in the
    /// tree — which is the ordinary outcome of a file being deleted between a
    /// sync and the ingestion job it queued.
    /// </summary>
    Task<RepositoryFileContent?> OpenFileAsync(
        string repositoryPath,
        CancellationToken ct = default);
}
