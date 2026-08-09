using System.Security.Cryptography;
using System.Text;
using DocHub.Integrations.SourceControl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Repository;

/// <summary>What a delivery from GitLab led to.</summary>
public enum WebhookOutcome
{
    /// <summary>The shared secret did not match, or none is configured.</summary>
    Rejected,

    /// <summary>
    /// Genuine, but nothing to do — a different branch, or an event the hub has
    /// no use for. Answered as success: GitLab retries and eventually disables
    /// a hook that keeps erroring, and there is no error here.
    /// </summary>
    Ignored,

    Queued,
}

/// <summary>
/// Decides what a GitLab delivery means.
///
/// A service rather than logic in the controller because every decision here is
/// a rule: whether the caller proved it is GitLab, whether the push touched the
/// branch being mirrored, and whether that warrants a sync. The controller only
/// turns the answer into a status code.
/// </summary>
public interface IRepositoryWebhook
{
    /// <param name="token">The <c>X-Gitlab-Token</c> header, verbatim.</param>
    /// <param name="eventName">The <c>X-Gitlab-Event</c> header, e.g. "Push Hook".</param>
    /// <param name="gitRef">The <c>ref</c> from the payload, e.g. "refs/heads/main".</param>
    WebhookOutcome Handle(string? token, string? eventName, string? gitRef);
}

internal sealed class RepositoryWebhook(
    IRepositorySyncQueue queue,
    ISourceRepositoryClient repository,
    IOptions<GitLabOptions> options,
    ILogger<RepositoryWebhook> logger) : IRepositoryWebhook
{
    private readonly GitLabOptions _options = options.Value;

    public WebhookOutcome Handle(string? token, string? eventName, string? gitRef)
    {
        // No secret configured refuses everything. This endpoint has to be
        // anonymous — GitLab cannot hold a session — so the secret is the only
        // thing separating GitLab from anyone else who can reach the box, and
        // an unset one has to fail closed for the same reason an empty Google
        // allow-list admits nobody.
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            logger.LogWarning(
                "Refused a GitLab webhook: GitLab:WebhookSecret is not configured.");
            return WebhookOutcome.Rejected;
        }

        if (!Matches(token, _options.WebhookSecret))
        {
            logger.LogWarning("Refused a GitLab webhook: the token did not match.");
            return WebhookOutcome.Rejected;
        }

        // Push hooks only. A pipeline, issue or comment hook pointed at this
        // address is a misconfiguration, not a reason to re-read the tree.
        if (!string.Equals(eventName, "Push Hook", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Ignoring GitLab '{Event}' hook.", eventName ?? "(none)");
            return WebhookOutcome.Ignored;
        }

        // A push to a feature branch changes nothing the hub mirrors, and
        // syncing on one would re-list the whole tree for every branch a team
        // pushes to — which on a busy repository is continuously.
        if (!IsMirroredBranch(gitRef))
        {
            logger.LogInformation(
                "Ignoring a push to '{Ref}'; mirroring '{Branch}'.",
                gitRef ?? "(none)", repository.Branch);
            return WebhookOutcome.Ignored;
        }

        queue.Enqueue(actorId: null);
        logger.LogInformation("Queued a sync from a push to {Ref}.", gitRef);

        return WebhookOutcome.Queued;
    }

    private bool IsMirroredBranch(string? gitRef)
    {
        if (string.IsNullOrWhiteSpace(gitRef)) return false;

        const string prefix = "refs/heads/";
        var branch = gitRef.StartsWith(prefix, StringComparison.Ordinal)
            ? gitRef[prefix.Length..]
            : gitRef;

        return string.Equals(branch, repository.Branch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Compared in fixed time. A token check that returns early on the first
    /// wrong character is guessable one character at a time by whoever can
    /// measure the difference.
    /// </summary>
    private static bool Matches(string? presented, string expected) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
}
