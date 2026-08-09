using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Activity;

/// <summary>
/// Records what happened, and reads it back for the dashboard.
///
/// Deliberately fire-and-forget from the caller's point of view: recording that
/// a file changed must never be the reason the sync fails. The trail is worth
/// having, but it is not worth more than the work it describes.
/// </summary>
public interface IActivityLog
{
    /// <summary>Records something the signed-in caller did.</summary>
    Task RecordAsync(
        ActivityType type,
        string target,
        Guid? targetId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records something with an explicit actor, for work that happens away
    /// from a request.
    /// </summary>
    /// <param name="actorId">
    /// Null when nobody caused it — a webhook sync, or an ingestion job the
    /// sync queued. Naming the seeded administrator instead would put a person
    /// against work that account did not do.
    /// </param>
    Task RecordForAsync(
        Guid? actorId,
        ActivityType type,
        string target,
        Guid? targetId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ActivityEventViewModel>> RecentAsync(
        int limit = 12,
        CancellationToken ct = default);
}

internal sealed class ActivityLog(
    IActivityRepository activity,
    ICurrentUser currentUser,
    ILogger<ActivityLog> logger) : IActivityLog
{
    public Task RecordAsync(
        ActivityType type,
        string target,
        Guid? targetId = null,
        CancellationToken ct = default) =>
        RecordForAsync(currentUser.Id, type, target, targetId, ct);

    public async Task RecordForAsync(
        Guid? actorId,
        ActivityType type,
        string target,
        Guid? targetId = null,
        CancellationToken ct = default)
    {
        try
        {
            await activity.AppendAsync(type, actorId, target, targetId, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Swallowed on purpose, and loudly. The alternative is a failed
            // audit write rolling back a successful sync, which trades a
            // missing feed entry for a repository that has to be mirrored again.
            logger.LogError(
                exception, "Could not record {Type} activity for {Target}", type, target);
        }
    }

    public async Task<IReadOnlyList<ActivityEventViewModel>> RecentAsync(
        int limit = 12,
        CancellationToken ct = default)
    {
        var events = await activity.RecentAsync(Math.Clamp(limit, 1, 50), ct);

        return [.. events.Select(entry => new ActivityEventViewModel(
            entry.Id,
            Describe(entry.Type),
            entry.Actor?.ToViewModel(),
            entry.Target,
            entry.TargetId,
            entry.At))];
    }

    /// <summary>
    /// The wire vocabulary the client already renders verbs for. Kept as an
    /// explicit map rather than a lowercased enum name so renaming a C# value
    /// cannot silently change the API.
    /// </summary>
    private static string Describe(ActivityType type) => type switch
    {
        ActivityType.Added => "added",
        ActivityType.Changed => "changed",
        ActivityType.Updated => "updated",
        ActivityType.Removed => "removed",
        ActivityType.Indexed => "indexed",
        ActivityType.Failed => "failed",
        ActivityType.Synced => "synced",
        _ => "updated",
    };
}
