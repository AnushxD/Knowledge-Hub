using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

/// <summary>Appends to, and reads back, the activity trail.</summary>
public interface IActivityRepository
{
    /// <param name="actorId">
    /// Null when nobody caused it — a webhook sync runs with no one signed in,
    /// and naming an account that did not do the work is worse than naming none.
    /// </param>
    Task AppendAsync(
        ActivityType type,
        Guid? actorId,
        string target,
        Guid? targetId,
        CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<ActivityEventDto>> RecentAsync(int take, CancellationToken ct = default);
}

internal sealed class ActivityRepository(DocHubDbContext db) : IActivityRepository
{
    public async Task AppendAsync(
        ActivityType type,
        Guid? actorId,
        string target,
        Guid? targetId,
        CancellationToken ct = default)
    {
        db.ActivityEvents.Add(new ActivityEvent
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            ActorId = actorId,
            // The column is capped; a pathological title must not fail the
            // write and take the operation it describes down with it.
            Target = target.Length <= 500 ? target : target[..497] + "…",
            TargetId = targetId,
            At = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ActivityEventDto>> RecentAsync(
        int take,
        CancellationToken ct = default)
    {
        return await db.ActivityEvents
            .AsNoTracking()
            .OrderByDescending(activity => activity.At)
            .Take(take)
            .Select(activity => new ActivityEventDto(
                activity.Id,
                activity.Type,
                activity.Actor == null
                    ? null
                    : new UserDto(
                        activity.Actor.Id,
                        activity.Actor.Name,
                        activity.Actor.Email ?? string.Empty,
                        activity.Actor.Role),
                activity.Target,
                activity.TargetId,
                activity.At))
            .ToListAsync(ct);
    }
}
