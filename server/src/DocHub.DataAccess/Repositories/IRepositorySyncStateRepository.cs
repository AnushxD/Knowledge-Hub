using DocHub.DataAccess.Dtos;

namespace DocHub.DataAccess.Repositories;

/// <summary>
/// Persistence for what the hub knows about its last mirror of a repository.
/// Written at both ends of a sync so the screen can tell "running" from
/// "finished" without the two sharing a process.
/// </summary>
public interface IRepositorySyncStateRepository
{
    /// <summary>The record for one project and branch, or null if none has ever run.</summary>
    Task<RepositorySyncStateDto?> GetAsync(
        string projectPath,
        string branch,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a sync as in flight, clearing the previous outcome. The counts from
    /// the last run go with it: leaving them would make a running sync look like
    /// it had already found those files.
    /// </summary>
    Task StartAsync(
        string projectPath,
        string branch,
        DateTimeOffset startedAt,
        CancellationToken ct = default);

    /// <summary>Records how a sync ended, whichever way it ended.</summary>
    Task FinishAsync(RepositorySyncStateDto state, CancellationToken ct = default);
}
