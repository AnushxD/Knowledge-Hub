using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// Reconciling the hub against the repository.
///
/// The fake repository is content-addressed like git, so "unchanged" and
/// "changed" are decided the same way sync decides them against GitLab —
/// which is the whole of what these tests are worth. A fake that handed out a
/// fresh blob id each call would make every assertion here pass for the wrong
/// reason.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class MirrorSyncTests(StackFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    [Fact]
    public async Task A_file_in_the_repository_becomes_a_document_in_its_own_folder()
    {
        await using var scope = fixture.NewScope();
        var directory = Unique("Docs");

        var created = await scope.PublishAsync($"{directory}/setup.md", "# Setup");

        Assert.Equal("setup", created.Title);
        Assert.Equal("setup.md", created.FileName);
        Assert.Equal("pending", created.Status);
        Assert.Equal($"{directory}/setup.md", created.RepositoryPath);

        // The folder tree is the repository's, under a single visible root
        // named for the project.
        var folders = await scope.Folders.GetAllAsync();
        Assert.Equal(
            $"docs/{directory}",
            folders.Single(folder => folder.Id == created.FolderId).Path);

        // And it links back to where it can actually be edited.
        Assert.Contains($"/-/blob/main/{directory}/setup.md", created.WebUrl);
    }

    [Fact]
    public async Task The_file_is_streamed_from_the_repository_rather_than_from_a_copy()
    {
        await using var scope = fixture.NewScope();
        var created = await scope.PublishAsync($"{Unique("Read")}/setup.md", "# Setup");

        var content = await scope.Documents.DownloadAsync(created.Id);
        using var reader = new StreamReader(content.Content);

        Assert.Equal("# Setup", await reader.ReadToEndAsync());
        Assert.Equal("setup.md", content.FileName);
        Assert.Equal("text/markdown", content.ContentType);
    }

    [Fact]
    public async Task An_unchanged_file_that_is_already_indexed_is_not_queued_again()
    {
        await using var scope = fixture.NewScope();
        var path = $"{Unique("Same")}/notes.md";

        // Indexed, not merely mirrored. That distinction is the whole point:
        // "unchanged" is about the file, and whether to re-queue is about the
        // document — see the pending case below.
        await scope.PublishIndexedAsync(path, "v1 body");
        var afterFirst = scope.Queue.Queued.Count;

        await scope.Mirror.SyncAsync(actorId: null);

        // Embedding is the expensive half of this product. A sync that
        // re-embedded everything it saw would make a repository of any size
        // unmirrorable, and the blob id is what makes it unnecessary.
        Assert.Equal(afterFirst, scope.Queue.Queued.Count);
    }

    [Fact]
    public async Task An_unchanged_file_that_never_finished_indexing_is_queued_again()
    {
        await using var scope = fixture.NewScope();
        var path = $"{Unique("Stalled")}/notes.md";

        // Mirrored and left Pending, which is exactly what a worker that stops
        // part way through a backlog leaves behind — a restart, a deploy, a
        // crash. The blob id will match for ever afterwards, so before this the
        // document was stranded: never searchable, and no sync would ever pick
        // it up. Measured against a real repository at 21 indexed of 636.
        var stranded = await scope.PublishAsync(path, "The gateway is at ten dot one.");
        Assert.Equal("pending", stranded.Status);

        var afterFirst = scope.Queue.Queued.Count;
        var repository = await scope.Mirror.SyncAsync(actorId: null);

        Assert.Contains(stranded.Id, scope.Queue.Queued);
        Assert.Equal(afterFirst + 1, scope.Queue.Queued.Count);

        // Counted, and counted apart from Updated: nothing in the repository
        // changed. A run reporting nothing but zeros while hundreds of
        // documents begin indexing reads as a run that did nothing.
        Assert.Equal(1, repository.Requeued);
        Assert.Equal(0, repository.Updated);
    }

    [Fact]
    public async Task An_unchanged_file_that_failed_permanently_is_left_alone()
    {
        await using var scope = fixture.NewScope();
        var path = $"{Unique("Broken")}/empty.md";

        // Whitespace only: extraction finds no text, which is the permanent
        // half of the failure split — it will fail identically every time.
        var failed = await scope.PublishIndexedAsync(path, "   \n  \n");
        Assert.Equal("failed", (await scope.Documents.GetAsync(failed.Id)).Document.Status);

        var afterFirst = scope.Queue.Queued.Count;
        var repository = await scope.Mirror.SyncAsync(actorId: null);

        // Retrying it on every sync would burn the queue on documents that can
        // only change when the repository holds a different revision — and that
        // arrives as a new blob id, which is requeued by the branch above.
        Assert.Equal(afterFirst, scope.Queue.Queued.Count);
        Assert.Equal(0, repository.Requeued);
    }

    [Fact]
    public async Task Changed_content_is_repointed_and_re_indexed()
    {
        await using var scope = fixture.NewScope();
        var path = $"{Unique("Edit")}/notes.md";

        var created = await scope.PublishIndexedAsync(path, "The gateway is at ten dot one.");
        Assert.Equal("indexed", (await scope.Documents.GetAsync(created.Id)).Document.Status);

        scope.Repository.Put(path, "The gateway moved to ten dot two.");
        await scope.Mirror.SyncAsync(actorId: null);

        var reloaded = await scope.Documents.GetAsync(created.Id);

        // Same document — the path is its identity — but back in the pipeline,
        // because the chunks describe a revision the repository no longer has.
        Assert.Equal(created.Id, reloaded.Document.Id);
        Assert.Equal("pending", reloaded.Document.Status);
        Assert.Contains(created.Id, scope.Queue.Queued);
    }

    [Fact]
    public async Task A_file_removed_from_the_repository_takes_its_chunks_with_it()
    {
        await using var scope = fixture.NewScope();
        var path = $"{Unique("Gone")}/deep.md";

        var created = await scope.PublishIndexedAsync(
            path, "## Retention\n\nAudit records are kept for seven years.");

        Assert.NotEmpty(await scope.Chunks.GetForDocumentAsync(created.Id));

        scope.Repository.Remove(path);
        await scope.Mirror.SyncAsync(actorId: null);

        await Assert.ThrowsAsync<NotFoundException>(() => scope.Documents.GetAsync(created.Id));

        // The seam a mocked repository would never catch: deleting the document
        // has to take its passages, or deleted content stays answerable.
        Assert.Empty(await scope.Chunks.GetForDocumentAsync(created.Id));
    }

    [Fact]
    public async Task A_type_no_extractor_can_read_is_counted_rather_than_mirrored()
    {
        await using var scope = fixture.NewScope();
        var directory = Unique("Code");

        scope.Repository.Put($"{directory}/Program.cs", "class Program { }");
        scope.Repository.Put($"{directory}/readme.md", "# Readme");

        var status = await scope.Mirror.SyncAsync(actorId: null);

        // A repository is mostly source code. Mirroring a .cs file would put a
        // row in the library that can never be searched and never stops saying
        // "failed" — so it is skipped, and the count says so out loud.
        Assert.Equal(1, status.Added);
        Assert.True(status.Skipped >= 1);

        var documents = await scope.Documents.QueryAsync(new DocumentQueryRequest { Take = 500 });
        Assert.DoesNotContain(documents, document => document.FileName == "Program.cs");
    }

    [Fact]
    public async Task A_failed_sync_leaves_the_mirror_exactly_as_it_was()
    {
        await using var scope = fixture.NewScope();
        var created = await scope.PublishAsync($"{Unique("Keep")}/kept.md", "# Kept");

        scope.Repository.Failure = new HttpRequestException("connection refused");
        var status = await scope.Mirror.SyncAsync(actorId: null);

        Assert.Equal("failed", status.Outcome);
        Assert.Contains("connection refused", status.Error);

        // Emptying the library because GitLab was briefly unreachable is far
        // worse than serving yesterday's tree, so nothing is removed.
        var still = await scope.Documents.GetAsync(created.Id);
        Assert.Equal(created.Id, still.Document.Id);
    }

    [Fact]
    public async Task A_failed_sync_does_not_advance_the_commit_the_mirror_claims()
    {
        await using var scope = fixture.NewScope();

        scope.Repository.Head = "aaaa";
        await scope.Mirror.SyncAsync(actorId: null);

        scope.Repository.Head = "bbbb";
        scope.Repository.Failure = new HttpRequestException("connection refused");
        await scope.Mirror.SyncAsync(actorId: null);

        // Claiming to be current with a commit it never finished reading would
        // make the next sync skip whatever it missed.
        Assert.Equal("aaaa", (await scope.Mirror.GetStatusAsync()).CommitSha);
    }

    [Fact]
    public async Task Updating_metadata_normalises_tags()
    {
        await using var scope = fixture.NewScope();
        var created = await scope.PublishAsync($"{Unique("Tag")}/t.md", "body");

        var updated = await scope.Documents.UpdateAsync(
            created.Id,
            new UpdateDocumentRequest(
                Title: "  Renamed  ",
                Description: null,
                Tags: ["#Setup", "setup", " Docker ", ""],
                IsStarred: true));

        // Hub-local metadata: the repository has no opinion about it, so it is
        // the one thing a reader can still edit.
        Assert.Equal("Renamed", updated.Title);
        Assert.Equal(["setup", "docker"], updated.Tags);
        Assert.True(updated.IsStarred);
    }

    [Fact]
    public async Task Hub_metadata_survives_the_file_changing_under_it()
    {
        await using var scope = fixture.NewScope();
        var path = $"{Unique("Meta")}/guide.md";

        var created = await scope.PublishAsync(path, "v1");
        await scope.Documents.UpdateAsync(
            created.Id, new UpdateDocumentRequest(null, "Team-written summary", ["ops"], true));

        scope.Repository.Put(path, "v2");
        await scope.Mirror.SyncAsync(actorId: null);

        var reloaded = (await scope.Documents.GetAsync(created.Id)).Document;

        // The path is the document's identity, so an edit to the file is not a
        // new document and does not discard what the team added around it.
        Assert.Equal("Team-written summary", reloaded.Description);
        Assert.Equal(["ops"], reloaded.Tags);
        Assert.True(reloaded.IsStarred);
    }

    [Fact]
    public async Task Queries_scope_to_a_folder_and_its_descendants()
    {
        await using var scope = fixture.NewScope();
        var directory = Unique("Scope");

        var top = await scope.PublishAsync($"{directory}/a.md", "a");
        await scope.PublishAsync($"{directory}/inner/b.md", "b");

        var recursive = await scope.Documents.QueryAsync(
            new DocumentQueryRequest { FolderId = top.FolderId });
        var direct = await scope.Documents.QueryAsync(
            new DocumentQueryRequest { FolderId = top.FolderId, Recursive = false });

        Assert.Equal(2, recursive.Count);
        Assert.Single(direct);
    }
}
