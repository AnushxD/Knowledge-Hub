using DocHub.Services.Activity;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

[ApiController]
[Route("api/activity")]
[Produces("application/json")]
public sealed class ActivityController(IActivityLog activity) : ControllerBase
{
    /// <summary>
    /// What has happened recently, newest first. Any signed-in user sees the
    /// whole library's activity — the library itself is not per-user, so a feed
    /// that hid other people's uploads would be a feed about nothing.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ActivityEventViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ActivityEventViewModel>> Recent(
        [FromQuery] int limit,
        CancellationToken ct) =>
        await activity.RecentAsync(limit <= 0 ? 12 : limit, ct);
}
