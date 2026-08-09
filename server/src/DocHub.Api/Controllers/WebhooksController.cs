using System.Text.Json.Serialization;
using DocHub.Services.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

/// <summary>
/// Endpoints only — accepts and returns ViewModels, holds no business logic.
/// Every decision about a delivery lives in <see cref="IRepositoryWebhook"/>.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[Produces("application/json")]
public sealed class WebhooksController(IRepositoryWebhook webhook) : ControllerBase
{
    /// <summary>
    /// Receives a GitLab push hook and queues a sync.
    ///
    /// Anonymous of necessity — GitLab holds no session — and therefore
    /// authenticated by the shared secret it sends back in
    /// <c>X-Gitlab-Token</c>. This is one of the very few endpoints outside the
    /// fallback policy, and the only one that causes work.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("gitlab")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GitLab([FromBody] GitLabPushHook? payload)
    {
        var outcome = webhook.Handle(
            Request.Headers["X-Gitlab-Token"].FirstOrDefault(),
            Request.Headers["X-Gitlab-Event"].FirstOrDefault(),
            payload?.Ref);

        return outcome switch
        {
            WebhookOutcome.Queued => Accepted(),
            // Understood and deliberately not acted on. GitLab disables a hook
            // that keeps failing, and "this push was for another branch" is not
            // a failure.
            WebhookOutcome.Ignored => NoContent(),
            _ => Unauthorized(),
        };
    }

    /// <summary>
    /// The one field of GitLab's push payload the hub reads.
    ///
    /// Deliberately not the whole schema: the commit list is not trusted to
    /// decide what changed — the tree is re-listed and blob ids compared — so
    /// binding it would be storing a claim the hub then ignores.
    /// </summary>
    public sealed record GitLabPushHook([property: JsonPropertyName("ref")] string? Ref);
}
