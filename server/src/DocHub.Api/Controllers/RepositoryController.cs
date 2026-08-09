using DocHub.Api.Infrastructure.Auth;
using DocHub.Services;
using DocHub.Services.Repository;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

/// <summary>
/// Endpoints only — accepts and returns ViewModels, holds no business logic.
/// </summary>
[ApiController]
[Route("api/repository")]
[Produces("application/json")]
public sealed class RepositoryController(
    IRepositoryMirrorService mirror,
    IRepositorySyncQueue queue,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Which repository the library comes from, and how current it is.
    ///
    /// Readable by anyone signed in, not just administrators: "is what I am
    /// reading up to date" is a question for whoever is reading, and hiding the
    /// answer behind a role is how a stale library passes for a fresh one.
    ///
    /// Deliberately cheap — one row and configuration, no call to GitLab — so
    /// the client can poll it while a sync runs.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<RepositoryViewModel>(StatusCodes.Status200OK)]
    public async Task<RepositoryViewModel> Get(CancellationToken ct) =>
        await mirror.GetStatusAsync(ct);

    /// <summary>
    /// Queues a sync and returns immediately with the state as it stands.
    ///
    /// Admin only. It is not destructive, but it is a job that can run for
    /// minutes against somebody else's GitLab instance, and the button for it
    /// belongs beside the rest of the deployment's controls.
    ///
    /// 202 rather than 200 even when a sync is already running: either way the
    /// answer is "it is being mirrored, watch this record", and reporting a
    /// conflict would invite the client to retry into the same state.
    /// </summary>
    [Authorize(Policy = Policies.Admin)]
    [HttpPost("sync")]
    [ProducesResponseType<RepositoryViewModel>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<RepositoryViewModel>> Sync(CancellationToken ct)
    {
        queue.Enqueue(currentUser.Id);
        return Accepted(await mirror.GetStatusAsync(ct));
    }
}
