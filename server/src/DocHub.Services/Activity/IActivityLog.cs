using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;

namespace DocHub.Services.Activity;

/// <summary>
/// Records what people do, and reads it back for the dashboard.
///
/// Deliberately fire-and-forget from the caller's point of view: recording
/// that a document was uploaded must never be the reason the upload fails.
/// The trail is worth having, but it is not worth more than the work it
/// describes.
/// </summary>
public interface IActivityLog
{
    Task RecordAsync(
        ActivityType type,
        string target,
        Guid? targetId = null,
        Guid? actorId = null,
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
    public async Task RecordAsync(
        ActivityType type,
        string target,
        Guid? targetId = null,
        Guid? actorId = null,
        CancellationToken ct = default)
    {
        try
        {
            // An explicit actor covers the unattended case: ingestion runs on a
            // background worker with nobody signed in, so it records the
            // document's owner rather than inventing a system identity.
            await activity.AppendAsync(type, actorId ?? currentUser.Id, target, targetId, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Swallowed on purpose, and loudly. The alternative is a failed
            // audit write rolling back a successful upload, which trades a
            // missing feed entry for lost work.
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
            entry.Actor.ToViewModel(),
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
        ActivityType.Uploaded => "uploaded",
        ActivityType.Updated => "updated",
        ActivityType.Moved => "moved",
        ActivityType.Deleted => "deleted",
        ActivityType.Indexed => "indexed",
        ActivityType.Failed => "failed",
        ActivityType.FolderCreated => "folder-created",
        ActivityType.FolderDeleted => "folder-deleted",
        _ => "updated",
    };
}
