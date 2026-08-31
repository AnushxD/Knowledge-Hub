using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DocHub.Integrations.SourceControl;

/// <summary>
/// What was established about a repository before committing to it.
/// </summary>
/// <param name="IsReachable">Something answered at the address. Nothing below means anything otherwise.</param>
/// <param name="ProjectFound">
/// The project exists and the token — or anonymous access — may read it. GitLab
/// answers 404 for a project that is there but not visible to the caller, so
/// this is deliberately "found and readable" rather than "exists".
/// </param>
/// <param name="BranchFound">The branch exists on that project.</param>
/// <param name="SubPathFound">
/// The sub-path names a directory with files in it. False for a sub-path that
/// is simply wrong — the most expensive mistake here, because a hub pointed at
/// an empty directory mirrors nothing and looks broken rather than misaimed.
/// </param>
/// <param name="Detail">What was actually established, in the words the screen shows.</param>
/// <param name="UsedToken">
/// Whether a token was sent. A public project reads fine without one, and
/// "worked, anonymously" is worth telling apart from "worked, authenticated".
/// </param>
public sealed record RepositoryConnection(
    bool IsReachable,
    bool ProjectFound,
    bool BranchFound,
    bool SubPathFound,
    string Detail,
    bool UsedToken,
    string? ProjectName = null,
    string? DefaultBranch = null,
    string? WebUrl = null);

/// <summary>
/// Checks a repository answers, before it is saved.
///
/// Takes the settings rather than reading them, because the useful moment to
/// test an address is while somebody is still typing it — the same reason the
/// MCP server probe takes an endpoint rather than a server's name.
/// </summary>
public interface IRepositoryConnectionProbe
{
    Task<RepositoryConnection> ProbeAsync(
        RepositoryConfiguration candidate,
        CancellationToken ct = default);
}

internal sealed class GitLabConnectionProbe(
    HttpClient http,
    ILogger<GitLabConnectionProbe> logger) : IRepositoryConnectionProbe
{
    public async Task<RepositoryConnection> ProbeAsync(
        RepositoryConfiguration candidate,
        CancellationToken ct = default)
    {
        var usedToken = !string.IsNullOrWhiteSpace(candidate.Token);

        if (!candidate.IsConfigured)
        {
            return new RepositoryConnection(
                IsReachable: false, ProjectFound: false, BranchFound: false, SubPathFound: false,
                "Enter an instance address and a project path first.", usedToken);
        }

        if (!Uri.TryCreate(candidate.BaseUrl, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            return new RepositoryConnection(
                IsReachable: false, ProjectFound: false, BranchFound: false, SubPathFound: false,
                "The instance address must be an absolute http or https URL, such as "
                + "'https://gitlab.example.org'.",
                usedToken);
        }

        var projectApi = $"{candidate.BaseUrl.TrimEnd('/')}/api/v4/projects/"
            + Uri.EscapeDataString(candidate.ProjectPath.Trim('/'));

        JsonDocument project;

        try
        {
            using var response = await SendAsync(projectApi, candidate, ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new RepositoryConnection(
                    IsReachable: true, ProjectFound: false, BranchFound: false, SubPathFound: false,
                    usedToken
                        ? "GitLab refused the token. It needs read_repository on this project."
                        : "This project is not public. Set an access token with read_repository.",
                    usedToken);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new RepositoryConnection(
                    IsReachable: true, ProjectFound: false, BranchFound: false, SubPathFound: false,
                    $"GitLab has no project '{candidate.ProjectPath}' the "
                    + (usedToken ? "token" : "hub")
                    + " can read. It is the namespaced path, as in 'team/docs'.",
                    usedToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new RepositoryConnection(
                    IsReachable: true, ProjectFound: false, BranchFound: false, SubPathFound: false,
                    $"GitLab answered {(int)response.StatusCode} {response.ReasonPhrase}.",
                    usedToken);
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            project = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogInformation(
                exception, "Repository probe could not reach {BaseUrl}", candidate.BaseUrl);

            // Nothing answered. Reported as the address being wrong or the
            // instance being unreachable, without guessing which — the message
            // GitLab's client would have thrown says more than a summary of it.
            return new RepositoryConnection(
                IsReachable: false, ProjectFound: false, BranchFound: false, SubPathFound: false,
                $"Could not reach GitLab at {candidate.BaseUrl}: {exception.Message}",
                usedToken);
        }

        using (project)
        {
            var name = Text(project.RootElement, "path_with_namespace")
                ?? Text(project.RootElement, "name");
            var defaultBranch = Text(project.RootElement, "default_branch");
            var webUrl = Text(project.RootElement, "web_url");

            var branchFound = await BranchExistsAsync(projectApi, candidate, ct);

            if (!branchFound)
            {
                return new RepositoryConnection(
                    IsReachable: true, ProjectFound: true, BranchFound: false, SubPathFound: false,
                    $"'{name}' was read, but it has no branch '{candidate.Branch}'."
                    + (defaultBranch is null ? string.Empty : $" Its default branch is '{defaultBranch}'."),
                    usedToken, name, defaultBranch, webUrl);
            }

            var subPath = candidate.SubPath.Trim('/');
            var subPathFound = subPath.Length == 0
                || await HasFilesAsync(projectApi, candidate, subPath, ct);

            if (!subPathFound)
            {
                return new RepositoryConnection(
                    IsReachable: true, ProjectFound: true, BranchFound: true, SubPathFound: false,
                    $"'{name}' and branch '{candidate.Branch}' were read, but '{subPath}' holds no "
                    + "files on that branch. The hub would mirror nothing.",
                    usedToken, name, defaultBranch, webUrl);
            }

            var where = subPath.Length == 0
                ? "the whole repository"
                : $"'{subPath}'";

            return new RepositoryConnection(
                IsReachable: true, ProjectFound: true, BranchFound: true, SubPathFound: true,
                $"Read '{name}' on branch '{candidate.Branch}'"
                + (usedToken ? " with the token" : " anonymously")
                + $". Mirroring {where}.",
                usedToken, name, defaultBranch, webUrl);
        }
    }

    private async Task<bool> BranchExistsAsync(
        string projectApi,
        RepositoryConfiguration candidate,
        CancellationToken ct)
    {
        var url = $"{projectApi}/repository/branches/"
            + Uri.EscapeDataString(candidate.Branch);

        using var response = await SendAsync(url, candidate, ct);

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// One page of one entry under the sub-path. Enough to tell "this directory
    /// has something in it" from "this directory is a typo", and far cheaper
    /// than the full recursive listing a sync does.
    /// </summary>
    private async Task<bool> HasFilesAsync(
        string projectApi,
        RepositoryConfiguration candidate,
        string subPath,
        CancellationToken ct)
    {
        var url = $"{projectApi}/repository/tree"
            + $"?ref={Uri.EscapeDataString(candidate.Branch)}"
            + $"&path={Uri.EscapeDataString(subPath)}&recursive=true&per_page=1";

        using var response = await SendAsync(url, candidate, ct);

        if (!response.IsSuccessStatusCode) return false;

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);

        return json.RootElement.ValueKind == JsonValueKind.Array
            && json.RootElement.GetArrayLength() > 0;
    }

    private Task<HttpResponseMessage> SendAsync(
        string url,
        RepositoryConfiguration candidate,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Omitted rather than sent empty, exactly as the client does it: GitLab
        // answers an empty PRIVATE-TOKEN with 401 instead of reading a public
        // project anonymously.
        if (!string.IsNullOrWhiteSpace(candidate.Token))
            request.Headers.Add("PRIVATE-TOKEN", candidate.Token);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return http.SendAsync(request, ct);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
