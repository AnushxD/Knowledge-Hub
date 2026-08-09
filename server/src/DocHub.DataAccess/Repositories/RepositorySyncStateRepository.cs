using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

internal sealed class RepositorySyncStateRepository(DocHubDbContext db)
    : IRepositorySyncStateRepository
{
    public async Task<RepositorySyncStateDto?> GetAsync(
        string projectPath,
        string branch,
        CancellationToken ct = default)
    {
        var state = await db.RepositorySyncStates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.ProjectPath == projectPath && candidate.Branch == branch,
                ct);

        return state is null ? null : ToDto(state);
    }

    public async Task StartAsync(
        string projectPath,
        string branch,
        DateTimeOffset startedAt,
        CancellationToken ct = default)
    {
        var state = await FindAsync(projectPath, branch, ct);

        if (state is null)
        {
            state = new RepositorySyncState { ProjectPath = projectPath, Branch = branch };
            db.RepositorySyncStates.Add(state);
        }

        state.Outcome = SyncOutcome.Running;
        state.StartedAt = startedAt;
        state.FinishedAt = null;
        state.Error = null;
        state.FilesAdded = 0;
        state.FilesUpdated = 0;
        state.FilesRemoved = 0;
        state.FilesSkipped = 0;

        // CommitSha is deliberately left alone: until this run succeeds, the
        // mirror is still current with whatever the last one brought in.

        await db.SaveChangesAsync(ct);
    }

    public async Task FinishAsync(RepositorySyncStateDto input, CancellationToken ct = default)
    {
        var state = await FindAsync(input.ProjectPath, input.Branch, ct);

        if (state is null)
        {
            state = new RepositorySyncState
            {
                ProjectPath = input.ProjectPath,
                Branch = input.Branch,
                StartedAt = input.StartedAt,
            };
            db.RepositorySyncStates.Add(state);
        }

        state.Outcome = input.Outcome;
        state.FinishedAt = input.FinishedAt;
        state.Error = Truncate.ToFit(input.Error, DocHubDbContext.FailureReasonMaxLength);
        state.FilesAdded = input.FilesAdded;
        state.FilesUpdated = input.FilesUpdated;
        state.FilesRemoved = input.FilesRemoved;
        state.FilesSkipped = input.FilesSkipped;

        // Only a successful run may move the recorded commit. A failed one has
        // mirrored some unknown fraction of the tree, and claiming it is
        // current with the head would make the next sync skip the rest.
        if (input.Outcome == SyncOutcome.Succeeded) state.CommitSha = input.CommitSha;

        await db.SaveChangesAsync(ct);
    }

    private Task<RepositorySyncState?> FindAsync(
        string projectPath,
        string branch,
        CancellationToken ct) =>
        db.RepositorySyncStates.FirstOrDefaultAsync(
            candidate => candidate.ProjectPath == projectPath && candidate.Branch == branch, ct);

    private static RepositorySyncStateDto ToDto(RepositorySyncState state) => new(
        state.ProjectPath,
        state.Branch,
        state.Outcome,
        state.CommitSha,
        state.StartedAt,
        state.FinishedAt,
        state.Error,
        state.FilesAdded,
        state.FilesUpdated,
        state.FilesRemoved,
        state.FilesSkipped);
}
