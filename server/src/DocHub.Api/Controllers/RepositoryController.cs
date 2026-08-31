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
    IRepositorySettingsAdmin settings,
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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RepositoryViewModel>> Sync(CancellationToken ct)
    {
        var status = await mirror.GetStatusAsync(ct);

        // Refused rather than queued into a job that can only fail: there is
        // nothing to mirror until the hub has been pointed somewhere, and the
        // message says where to do that.
        if (!status.IsConfigured)
        {
            throw new ValidationException(
                "No repository is configured. Point the hub at one under Settings first.");
        }

        queue.Enqueue(currentUser.Id);
        return Accepted(status);
    }

    // ---- which repository is mirrored ----------------------------------------
    //
    // Admin only, and separate from the status above for the same reason the
    // knowledge-source endpoints are split: this one names the instance and
    // says whether a credential is held, while that one only says how current
    // the library is — a question for whoever is reading it.

    /// <summary>
    /// The repository settings in force. Secrets are described, never returned:
    /// there is no screen that needs to show a token back.
    /// </summary>
    [Authorize(Policy = Policies.Admin)]
    [HttpGet("settings")]
    [ProducesResponseType<RepositorySettingsViewModel>(StatusCodes.Status200OK)]
    public async Task<RepositorySettingsViewModel> GetSettings(CancellationToken ct) =>
        await settings.GetAsync(ct);

    /// <summary>
    /// Points the hub at a repository. In force immediately — the next sync,
    /// webhook and file fetch use it, with no restart.
    ///
    /// Does not sync. Changing the project or branch replaces the whole library
    /// at the next one, and that is a decision worth pressing a second button
    /// for rather than a side effect of saving a form.
    /// </summary>
    [Authorize(Policy = Policies.Admin)]
    [HttpPut("settings")]
    [ProducesResponseType<RepositorySettingsViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<RepositorySettingsViewModel> SaveSettings(
        [FromBody] UpdateRepositorySettingsRequest request,
        CancellationToken ct) =>
        await settings.SaveAsync(request, ct);

    /// <summary>
    /// Reads the repository described by the request without saving it, so a
    /// wrong project path or sub-path is caught before it empties the library.
    /// </summary>
    [Authorize(Policy = Policies.Admin)]
    [HttpPost("settings/test")]
    [ProducesResponseType<RepositoryConnectionViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<RepositoryConnectionViewModel> TestSettings(
        [FromBody] UpdateRepositorySettingsRequest request,
        CancellationToken ct) =>
        await settings.TestAsync(request, ct);
}
