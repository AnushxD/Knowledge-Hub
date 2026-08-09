using System.Diagnostics;
using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.SourceControl;
using DocHub.Services.Activity;
using DocHub.Services.Ingestion;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Repository;

internal sealed class RepositoryMirrorService(
    ISourceRepositoryClient repository,
    IDocumentRepository documents,
    IFolderRepository folders,
    IRepositorySyncStateRepository syncState,
    IIngestionService ingestion,
    IIngestionQueue queue,
    IActivityLog activity,
    IOptions<GitLabOptions> options,
    ILogger<RepositoryMirrorService> logger) : IRepositoryMirrorService
{
    private readonly GitLabOptions _options = options.Value;

    /// <summary>
    /// One sync at a time, process-wide.
    ///
    /// Static because the service is scoped and a webhook burst — GitLab sends
    /// one request per push, and a merge is several pushes — would otherwise
    /// run several reconciliations over the same tree concurrently. They would
    /// race on the unique repository path: both would see a file missing, both
    /// would insert it, and one would lose.
    ///
    /// A single box is the whole deployment, so this is sufficient. Two API
    /// instances against one database would need a database-level lock instead.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<RepositoryViewModel> GetStatusAsync(CancellationToken ct = default)
    {
        var state = await syncState.GetAsync(repository.ProjectPath, repository.Branch, ct);
        return ToViewModel(state);
    }

    public async Task<RepositoryViewModel> SyncAsync(
        Guid? actorId,
        CancellationToken ct = default)
    {
        // Refused rather than queued. A caller who presses "Sync now" twice
        // wants the repository mirrored, not mirrored twice, and the second
        // answer is the same as the first.
        if (!await Gate.WaitAsync(TimeSpan.Zero, ct))
        {
            logger.LogInformation("Sync already running; ignoring the request to start another.");
            return await GetStatusAsync(ct);
        }

        try
        {
            return await RunAsync(actorId, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<RepositoryViewModel> RunAsync(Guid? actorId, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        await syncState.StartAsync(repository.ProjectPath, repository.Branch, startedAt, ct);

        try
        {
            var head = await repository.GetHeadCommitAsync(ct);
            var files = await repository.ListFilesAsync(ct);

            var outcome = await ReconcileAsync(files, head, ct);

            var finished = new RepositorySyncStateDto(
                repository.ProjectPath,
                repository.Branch,
                SyncOutcome.Succeeded,
                head,
                startedAt,
                DateTimeOffset.UtcNow,
                Error: null,
                outcome.Added,
                outcome.Updated,
                outcome.Removed,
                outcome.Skipped);

            await syncState.FinishAsync(finished, ct);

            // One entry for the whole run, not one per file. A push touching
            // two hundred files would otherwise be the entire feed, burying
            // everything a person did.
            await activity.RecordForAsync(
                actorId,
                ActivityType.Synced,
                $"{repository.ProjectPath}@{repository.Branch}",
                targetId: null,
                ct);

            logger.LogInformation(
                "Synced {Project}@{Branch} at {Commit}: {Added} added, {Updated} updated, "
                + "{Removed} removed, {Skipped} not indexable, in {ElapsedMs}ms",
                repository.ProjectPath, repository.Branch, head ?? "(no commits)",
                outcome.Added, outcome.Updated, outcome.Removed, outcome.Skipped,
                stopwatch.ElapsedMilliseconds);

            return ToViewModel(finished);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The mirror is deliberately left as it was. A sync that fails part
            // way through listing has no idea which files are gone and which it
            // simply never saw, and emptying the library because GitLab was
            // briefly unreachable is far worse than serving yesterday's tree.
            logger.LogError(
                exception, "Sync of {Project}@{Branch} failed",
                repository.ProjectPath, repository.Branch);

            var failed = new RepositorySyncStateDto(
                repository.ProjectPath,
                repository.Branch,
                SyncOutcome.Failed,
                CommitSha: null,
                startedAt,
                DateTimeOffset.UtcNow,
                exception.Message,
                FilesAdded: 0,
                FilesUpdated: 0,
                FilesRemoved: 0,
                FilesSkipped: 0);

            await syncState.FinishAsync(failed, CancellationToken.None);

            return ToViewModel(await syncState.GetAsync(
                repository.ProjectPath, repository.Branch, CancellationToken.None));
        }
    }

    private async Task<SyncCounts> ReconcileAsync(
        IReadOnlyList<RepositoryFile> files,
        string? head,
        CancellationToken ct)
    {
        var indexable = new List<RepositoryFile>();
        var skipped = 0;

        foreach (var file in files)
        {
            // A repository is mostly source code. Mirroring a .cs file the
            // extractors cannot read would put a row in the library that can
            // never be searched, never be previewed and never stop saying
            // "failed" — a permanent error for a file that is simply not
            // documentation.
            if (ingestion.SupportedExtensions.Contains(ExtensionOf(file.Name)))
                indexable.Add(file);
            else
                skipped++;
        }

        var mirrored = (await documents.GetMirrorAsync(ct))
            .ToDictionary(file => file.RepositoryPath, StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        var counts = new SyncCounts { Skipped = skipped };
        var unchanged = new List<Guid>();

        // Departures are settled before the folder tree is touched, and it has
        // to stay that way. Reconciling first would remove the directories a
        // departed file lived in, and the cascade would take the document with
        // them — leaving nothing to count and nothing to name in the trail. The
        // library would empty itself in silence.
        var present = indexable.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var departed = mirrored.Values.Where(file => !present.Contains(file.RepositoryPath)).ToList();

        if (departed.Count > 0)
        {
            // Files that stopped being indexable — a rename from .md to .cs —
            // land here too, which is right: the hub can no longer search them,
            // so keeping the row would promise otherwise.
            await documents.DeleteManyAsync([.. departed.Select(file => file.Id)], ct);
            counts.Removed = departed.Count;

            foreach (var file in departed)
            {
                // No target id: the document is gone, and a link to it would
                // only ever lead to "not found".
                await activity.RecordForAsync(
                    actorId: null, ActivityType.Removed, file.Title, targetId: null, ct);
            }
        }

        var folderIds = await folders.ReconcileAsync(
            [.. indexable
                .Select(file => FolderPathFor(file.Path))
                .Distinct(StringComparer.Ordinal)],
            ct);

        foreach (var file in indexable)
        {
            if (!mirrored.TryGetValue(file.Path, out var existing))
            {
                await AddAsync(file, folderIds, head, ct);
                counts.Added++;
                continue;
            }

            if (existing.BlobSha == file.BlobSha)
            {
                unchanged.Add(existing.Id);
                continue;
            }

            await documents.SetContentAsync(existing.Id, file.BlobSha, head, ct);
            queue.Enqueue(existing.Id);

            await activity.RecordForAsync(
                actorId: null, ActivityType.Changed, existing.Title, existing.Id, ct);

            counts.Updated++;
        }

        await documents.TouchAsync(unchanged, now, ct);

        return counts;
    }

    private async Task AddAsync(
        RepositoryFile file,
        IReadOnlyDictionary<string, Guid> folderIds,
        string? head,
        CancellationToken ct)
    {
        var directory = FolderPathFor(file.Path);

        if (!folderIds.TryGetValue(directory, out var folderId))
        {
            // Reconciliation was handed every directory these files sit in, so
            // this cannot happen. Logged rather than thrown: losing one file is
            // better than abandoning the sync, and the sync record will not add
            // up, which is the signal worth having.
            logger.LogWarning(
                "No folder for '{Directory}', so '{Path}' was not mirrored.",
                directory, file.Path);
            return;
        }

        var created = await documents.CreateAsync(
            new NewDocumentDto
            {
                FolderId = folderId,
                Title = Path.GetFileNameWithoutExtension(file.Name),
                FileName = file.Name,
                Extension = ExtensionOf(file.Name),
                ContentType = FileContentTypes.For(ExtensionOf(file.Name)),
                RepositoryPath = file.Path,
                BlobSha = file.BlobSha,
                CommitSha = head,
            },
            ct);

        await activity.RecordForAsync(
            actorId: null, ActivityType.Added, created.Title, created.Id, ct);

        // Queued rather than awaited: extracting and embedding a repository's
        // worth of documents takes minutes, and a sync request should not.
        queue.Enqueue(created.Id);
    }

    /// <summary>
    /// The folder a mirrored file belongs in, as a materialised path.
    ///
    /// Everything hangs off a single visible root named for the mirrored
    /// directory, or for the project when the whole repository is mirrored.
    /// Without it a file at the top of the tree would have no folder at all,
    /// and the sidebar would show loose files above the first directory.
    /// </summary>
    private string FolderPathFor(string repositoryPath)
    {
        var cut = repositoryPath.LastIndexOf('/');
        return cut < 0 ? RootFolderName : $"{RootFolderName}/{repositoryPath[..cut]}";
    }

    private string RootFolderName
    {
        get
        {
            var subPath = _options.SubPath.Trim('/');
            if (subPath.Length > 0) return subPath[(subPath.LastIndexOf('/') + 1)..];

            var project = repository.ProjectPath.Trim('/');
            return project[(project.LastIndexOf('/') + 1)..];
        }
    }

    private static string ExtensionOf(string fileName) =>
        Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

    private RepositoryViewModel ToViewModel(RepositorySyncStateDto? state) => new(
        repository.ProjectPath,
        repository.Branch,
        _options.SubPath,
        $"{_options.BaseUrl.TrimEnd('/')}/{repository.ProjectPath.Trim('/')}",
        // "never" rather than a missing record: a hub that has not synced looks
        // exactly like one whose repository is empty, and the fixes differ.
        state is null ? "never" : state.Outcome.ToString().ToLowerInvariant(),
        state?.CommitSha,
        state?.StartedAt,
        state?.FinishedAt,
        state?.Error,
        state?.FilesAdded ?? 0,
        state?.FilesUpdated ?? 0,
        state?.FilesRemoved ?? 0,
        state?.FilesSkipped ?? 0);

    private sealed class SyncCounts
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Removed { get; set; }
        public int Skipped { get; init; }
    }
}
