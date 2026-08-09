using System.Security.Cryptography;
using System.Text;
using DocHub.Integrations.SourceControl;

namespace DocHub.Services.Tests;

/// <summary>
/// A repository held in memory, standing in for GitLab.
///
/// Content-addressed the way git is — the blob id is a hash of the bytes — so
/// the thing sync actually depends on is real: writing identical content twice
/// produces one id and changes nothing, and that is what a test asserting "an
/// unchanged file is not re-embedded" needs to be true rather than stubbed.
/// </summary>
public sealed class FakeSourceRepository : ISourceRepositoryClient
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public string ProjectPath { get; set; } = "team/docs";

    public string Branch { get; set; } = "main";

    /// <summary>The commit reported as the head. Bump it to simulate a push.</summary>
    public string? Head { get; set; } = "0000000000000000000000000000000000000001";

    /// <summary>Set to make every call throw, standing in for GitLab being down.</summary>
    public Exception? Failure { get; set; }

    /// <summary>How many times the tree has been listed, to prove a sync ran once.</summary>
    public int ListCount { get; private set; }

    public void Put(string path, string content) =>
        _files[path.Trim('/')] = Encoding.UTF8.GetBytes(content);

    public void Put(string path, byte[] content) => _files[path.Trim('/')] = content;

    public void Remove(string path) => _files.Remove(path.Trim('/'));

    public void Clear() => _files.Clear();

    public Uri WebUrlFor(string repositoryPath) =>
        new($"https://gitlab.test/{ProjectPath}/-/blob/{Branch}/{repositoryPath}");

    public Task<string?> GetHeadCommitAsync(CancellationToken ct = default)
    {
        Throw();
        return Task.FromResult(Head);
    }

    public Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(CancellationToken ct = default)
    {
        Throw();
        ListCount++;

        IReadOnlyList<RepositoryFile> tree =
        [
            .. _files.Select(entry => new RepositoryFile(
                entry.Key,
                entry.Key[(entry.Key.LastIndexOf('/') + 1)..],
                Convert.ToHexString(SHA1.HashData(entry.Value)).ToLowerInvariant())),
        ];

        return Task.FromResult(tree);
    }

    public Task<RepositoryFileContent?> OpenFileAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        Throw();

        if (!_files.TryGetValue(repositoryPath.Trim('/'), out var bytes))
            return Task.FromResult<RepositoryFileContent?>(null);

        return Task.FromResult<RepositoryFileContent?>(
            new RepositoryFileContent(new MemoryStream(bytes), bytes.LongLength));
    }

    private void Throw()
    {
        if (Failure is not null) throw Failure;
    }
}
