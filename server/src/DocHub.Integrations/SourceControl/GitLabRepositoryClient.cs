using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations.SourceControl;

/// <summary>
/// The file is bigger than the configured ceiling. A permanent condition for a
/// given revision, not a transient one — separated from
/// <see cref="SourceRepositoryException"/> so ingestion records it and stops
/// rather than retrying a download that will always be refused.
/// </summary>
public sealed class FileTooLargeException(string message) : Exception(message);

/// <summary>
/// Reads a GitLab project over the v4 REST API.
///
/// Authenticated with <c>PRIVATE-TOKEN</c>, which accepts a personal, project
/// or group access token alike — the hub does not care which, only that it can
/// read. No OAuth flow: this is a server talking to a server on the same
/// network, with no user to redirect.
/// </summary>
internal sealed partial class GitLabRepositoryClient(
    HttpClient http,
    IOptions<GitLabOptions> options,
    ILogger<GitLabRepositoryClient> logger) : ISourceRepositoryClient
{
    private readonly GitLabOptions _options = options.Value;

    /// <summary>
    /// Guards the pagination loop. A tree of 50,000 files is already beyond
    /// what this hub is for; a loop that never ends because a server keeps
    /// advertising a next page is worse than a wrong answer.
    /// </summary>
    private const int MaxPages = 500;

    private const int PageSize = 100;

    public string ProjectPath => _options.ProjectPath;

    public string Branch => _options.Branch;

    public Uri WebUrlFor(string repositoryPath) =>
        new($"{_options.BaseUrl.TrimEnd('/')}/{_options.ProjectPath.Trim('/')}/-/blob/"
            + $"{Uri.EscapeDataString(_options.Branch)}/{FullPath(repositoryPath)}");

    public async Task<string?> GetHeadCommitAsync(CancellationToken ct = default)
    {
        var url = $"{ProjectApi}/repository/commits"
            + $"?ref_name={Uri.EscapeDataString(_options.Branch)}&per_page=1";

        using var response = await SendAsync(url, ct);
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);

        // An empty array is a branch with no commits — a real state, and not an
        // error. Everything downstream treats a null head as "nothing to mirror".
        if (json.RootElement.ValueKind != JsonValueKind.Array) return null;

        foreach (var commit in json.RootElement.EnumerateArray())
        {
            if (commit.TryGetProperty("id", out var id)) return id.GetString();
        }

        return null;
    }

    public async Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(CancellationToken ct = default)
    {
        var subPath = _options.SubPath.Trim('/');
        var prefix = subPath.Length == 0 ? string.Empty : subPath + "/";

        var url = $"{ProjectApi}/repository/tree"
            + $"?ref={Uri.EscapeDataString(_options.Branch)}"
            + $"&recursive=true&per_page={PageSize}&pagination=keyset";

        if (subPath.Length > 0) url += $"&path={Uri.EscapeDataString(subPath)}";

        var files = new List<RepositoryFile>();
        var pages = 0;
        string? next = url;

        while (next is not null)
        {
            if (++pages > MaxPages)
            {
                throw new SourceRepositoryException(
                    $"The tree of '{_options.ProjectPath}' did not end after {MaxPages} pages "
                    + "of 100 entries. Mirror a sub-path instead of the whole repository.");
            }

            using var response = await SendAsync(next, ct);
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);

            if (json.RootElement.ValueKind != JsonValueKind.Array) break;

            foreach (var entry in json.RootElement.EnumerateArray())
            {
                // Trees are the directories themselves and commits are
                // submodules. Neither has content to index, and the folder
                // hierarchy is derived from the file paths anyway.
                if (!entry.TryGetProperty("type", out var type) || type.GetString() != "blob")
                    continue;

                var path = entry.TryGetProperty("path", out var pathValue)
                    ? pathValue.GetString()
                    : null;
                var name = entry.TryGetProperty("name", out var nameValue)
                    ? nameValue.GetString()
                    : null;
                var sha = entry.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;

                if (path is null || name is null || sha is null) continue;

                // Paths come back from the root of the repository; the hub
                // stores them relative to the sub-path, so that repointing
                // SubPath does not rewrite every stored path by a prefix.
                if (prefix.Length > 0)
                {
                    if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    path = path[prefix.Length..];
                }

                files.Add(new RepositoryFile(path, name, sha));
            }

            next = NextPageUrl(response);
        }

        logger.LogInformation(
            "Listed {FileCount} files from {Project}@{Branch}{SubPath}",
            files.Count, _options.ProjectPath, _options.Branch,
            subPath.Length == 0 ? string.Empty : $" under {subPath}");

        return files;
    }

    public async Task<RepositoryFileContent?> OpenFileAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        // The whole path is one path segment to GitLab, slashes included, so it
        // is escaped as data rather than as a path.
        var url = $"{ProjectApi}/repository/files/"
            + $"{Uri.EscapeDataString(FullPath(repositoryPath))}/raw"
            + $"?ref={Uri.EscapeDataString(_options.Branch)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authenticate(request);

        HttpResponseMessage response;

        try
        {
            // Headers-only, so the size can be refused before a single byte of
            // a 400 MB binary is pulled across the network.
            response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !ct.IsCancellationRequested)
        {
            throw new SourceRepositoryException(
                $"Could not reach GitLab at {_options.BaseUrl}: {exception.Message}", exception);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        try
        {
            EnsureSuccess(response, url);

            var size = response.Content.Headers.ContentLength;

            if (size > _options.MaxFileBytes)
            {
                throw new FileTooLargeException(
                    $"The file is {size / 1024 / 1024} MB, above the "
                    + $"{_options.MaxFileBytes / 1024 / 1024} MB limit set by "
                    + "GitLab:MaxFileBytes, so it was not indexed.");
            }

            var content = await response.Content.ReadAsStreamAsync(ct);

            // The response owns the stream, so it must outlive this method —
            // the returned record disposes the stream, and disposing an
            // HttpContent stream releases the connection.
            return new RepositoryFileContent(content, size);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Project path escaped into the single id segment the API expects.</summary>
    private string ProjectApi =>
        $"{_options.BaseUrl.TrimEnd('/')}/api/v4/projects/"
        + Uri.EscapeDataString(_options.ProjectPath.Trim('/'));

    /// <summary>Path from the repository root, which is what GitLab addresses.</summary>
    private string FullPath(string repositoryPath)
    {
        var subPath = _options.SubPath.Trim('/');
        var path = repositoryPath.TrimStart('/');
        return subPath.Length == 0 ? path : $"{subPath}/{path}";
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authenticate(request);

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !ct.IsCancellationRequested)
        {
            throw new SourceRepositoryException(
                $"Could not reach GitLab at {_options.BaseUrl}: {exception.Message}", exception);
        }

        try
        {
            EnsureSuccess(response, url);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private void Authenticate(HttpRequestMessage request)
    {
        // Left off entirely for a public project rather than sent empty, which
        // GitLab answers with 401 instead of falling back to anonymous access.
        if (!string.IsNullOrWhiteSpace(_options.Token))
            request.Headers.Add("PRIVATE-TOKEN", _options.Token);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode) return;

        // The three that actually happen get their own sentence, because the
        // fix differs and "403 Forbidden" does not say which one it is.
        var explanation = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "GitLab refused the token. Check GitLab:Token has read_repository on "
                + $"'{_options.ProjectPath}'.",
            HttpStatusCode.NotFound =>
                $"GitLab has no project '{_options.ProjectPath}' or no branch "
                + $"'{_options.Branch}' on it. Check GitLab:ProjectPath and GitLab:Branch.",
            _ => $"GitLab answered {(int)response.StatusCode} {response.ReasonPhrase}.",
        };

        throw new SourceRepositoryException($"{explanation} ({url})");
    }

    /// <summary>
    /// The next page, from the <c>Link</c> header. GitLab sends one under both
    /// keyset and offset pagination, so following it works whichever mode the
    /// instance actually honoured.
    /// </summary>
    private static string? NextPageUrl(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var links)) return null;

        foreach (var header in links)
        {
            var match = NextLink().Match(header);
            if (match.Success) return match.Groups[1].Value;
        }

        return null;
    }

    [GeneratedRegex("<([^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLink();
}
