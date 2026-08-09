using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;

namespace DocHub.DataAccess.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class DocumentRepositoryTests(PostgresFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..20];

    /// <summary>A file in the repository, as sync would report it.</summary>
    private static NewDocumentDto NewDocument(Guid folderId, string directory, string title) => new()
    {
        FolderId = folderId,
        Title = title,
        FileName = $"{title.ToLowerInvariant()}.md",
        Extension = "md",
        ContentType = "text/markdown",
        RepositoryPath = $"{directory}/{title.ToLowerInvariant()}.md",
        BlobSha = Guid.NewGuid().ToString("N"),
        CommitSha = "0000000000000000000000000000000000000001",
    };

    /// <summary>
    /// Adds a directory straight to the table.
    ///
    /// Deliberately not through <c>ReconcileAsync</c>: that is authoritative
    /// over the whole tree and removes every directory absent from its list, so
    /// calling it to set one test up would delete the folders of every test
    /// that ran before it in this shared database. Reconciliation gets its own
    /// tests, where being authoritative is the point.
    /// </summary>
    private static async Task<Guid> FolderAsync(
        DocHubDbContext db,
        string path,
        Guid? parentId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var folder = new Folder
        {
            Id = Guid.CreateVersion7(),
            ParentId = parentId,
            Name = path[(path.LastIndexOf('/') + 1)..],
            Path = path,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        return folder.Id;
    }

    [Fact]
    public async Task CreateAsync_starts_pending_with_no_size_until_the_file_is_fetched()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Docs");
        var folder = await FolderAsync(db, directory);
        var created = await documents.CreateAsync(NewDocument(folder, directory, "Runbook"));

        Assert.Equal(IngestionStatus.Pending, created.Status);
        Assert.Null(created.ChunkCount);

        // The tree listing carries no size, and asking for one per file would
        // be a round trip per file on every sync. Ingestion fills it in.
        Assert.Equal(0, created.SizeBytes);

        var detail = await documents.GetByIdAsync(created.Id);
        Assert.NotNull(detail);
        Assert.Equal($"{directory}/runbook.md", detail.Document.RepositoryPath);
    }

    [Fact]
    public async Task QueryAsync_with_recursive_scope_includes_descendant_folders()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Tree");
        var root = await FolderAsync(db, directory);
        var nested = await FolderAsync(db, $"{directory}/Nested", root);

        await documents.CreateAsync(NewDocument(root, directory, "AtRoot"));
        await documents.CreateAsync(NewDocument(nested, $"{directory}/Nested", "AtChild"));

        var recursive = await documents.QueryAsync(new DocumentQueryDto { FolderId = root });
        var direct = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = root, Recursive = false });

        Assert.Equal(2, recursive.Count);
        Assert.Single(direct);
        Assert.Equal("AtRoot", direct[0].Title);
    }

    [Fact]
    public async Task QueryAsync_filters_by_tag_status_and_text()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Filter");
        var folder = await FolderAsync(db, directory);

        var postgres = await documents.CreateAsync(
            NewDocument(folder, directory, "PostgresSetup"));
        await documents.CreateAsync(NewDocument(folder, directory, "HangfireTriage"));

        // Tags are hub-local and applied after the fact — nothing in the
        // repository corresponds to one, so a mirrored file arrives untagged.
        await documents.UpdateMetadataAsync(
            postgres.Id, new DocumentMetadataUpdateDto { Tags = ["database", "setup"] });

        await documents.SetStatusAsync(postgres.Id, IngestionStatus.Indexed, chunkCount: 12);

        var byTag = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = folder, Tags = ["database"] });
        var byStatus = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = folder, Statuses = [IngestionStatus.Indexed] });
        // Deliberately different casing — filtering must be case-insensitive.
        var byText = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = folder, Text = "postgres" });

        Assert.Equal("PostgresSetup", Assert.Single(byTag).Title);
        Assert.Equal(12, Assert.Single(byStatus).ChunkCount);
        Assert.Equal("PostgresSetup", Assert.Single(byText).Title);
    }

    [Fact]
    public async Task QueryAsync_matches_on_the_repository_path()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Path");
        var folder = await FolderAsync(db, directory);
        await documents.CreateAsync(NewDocument(folder, directory, "Anything"));

        // The path is how people refer to a file in a repository, so searching
        // the library by it has to work even when the title shares nothing.
        var found = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = folder, Text = directory });

        Assert.Single(found);
    }

    [Fact]
    public async Task SetContentAsync_repoints_the_blob_and_sends_it_back_for_ingestion()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Rev");
        var folder = await FolderAsync(db, directory);
        var created = await documents.CreateAsync(NewDocument(folder, directory, "Policy"));

        await documents.SetStatusAsync(
            created.Id, IngestionStatus.Indexed, chunkCount: 9, sizeBytes: 2048);

        var updated = await documents.SetContentAsync(created.Id, "beefbeef", "cafecafe");

        Assert.NotNull(updated);
        Assert.Equal("beefbeef", updated.BlobSha);
        Assert.Equal("cafecafe", updated.CommitSha);

        // New content invalidates the old chunks, so it must re-ingest — and
        // the recorded size described the previous revision.
        Assert.Equal(IngestionStatus.Pending, updated.Status);
        Assert.Null(updated.ChunkCount);
        Assert.Equal(0, updated.SizeBytes);
    }

    [Fact]
    public async Task Two_documents_cannot_share_one_repository_path()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Dup");
        var folder = await FolderAsync(db, directory);
        await documents.CreateAsync(NewDocument(folder, directory, "Same"));

        // The path is the document's identity. Two rows for one file would mean
        // the same passage cited twice under two different ids, so the database
        // is what guarantees it rather than sync's own bookkeeping.
        await Assert.ThrowsAnyAsync<Exception>(
            () => documents.CreateAsync(NewDocument(folder, directory, "Same")));
    }

    [Fact]
    public async Task SetStatusAsync_keeps_a_failure_reason_only_while_failed()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Fail");
        var folder = await FolderAsync(db, directory);
        var created = await documents.CreateAsync(NewDocument(folder, directory, "Scanned"));

        var failed = await documents.SetStatusAsync(
            created.Id, IngestionStatus.Failed, "No extractable text.");
        Assert.Equal("No extractable text.", failed!.FailureReason);

        var retried = await documents.SetStatusAsync(created.Id, IngestionStatus.Indexing);
        Assert.Null(retried!.FailureReason);
    }

    [Fact]
    public async Task SetStatusAsync_shortens_a_failure_reason_too_long_for_its_column()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Long");
        var folder = await FolderAsync(db, directory);
        var created = await documents.CreateAsync(NewDocument(folder, directory, "Runaway"));

        // Exception messages are not length-checked anywhere upstream. If this
        // overflowed it would throw out of the handler recording the failure,
        // replacing a reported problem with an unreported one.
        var reason = new string('e', DocHubDbContext.FailureReasonMaxLength + 500);

        var failed = await documents.SetStatusAsync(created.Id, IngestionStatus.Failed, reason);

        Assert.Equal(DocHubDbContext.FailureReasonMaxLength, failed!.FailureReason!.Length);
        Assert.EndsWith("…", failed.FailureReason);
    }

    [Fact]
    public async Task GetMirrorAsync_returns_the_path_and_revision_of_every_file()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Diff");
        var folder = await FolderAsync(db, directory);
        var created = await documents.CreateAsync(NewDocument(folder, directory, "Known"));

        var mirror = await documents.GetMirrorAsync();
        var entry = Assert.Single(mirror, file => file.Id == created.Id);

        // What sync diffs against. A whole DocumentDto each would pull a
        // repository's worth of descriptions and tags to compare two strings.
        Assert.Equal(created.RepositoryPath, entry.RepositoryPath);
        Assert.Equal(created.BlobSha, entry.BlobSha);
        Assert.Equal("Known", entry.Title);
    }

    [Fact]
    public async Task DeleteManyAsync_removes_the_documents_and_reports_how_many_went()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Del");
        var folder = await FolderAsync(db, directory);
        var first = await documents.CreateAsync(NewDocument(folder, directory, "First"));
        var second = await documents.CreateAsync(NewDocument(folder, directory, "Second"));

        var removed = await documents.DeleteManyAsync([first.Id, second.Id]);

        Assert.Equal(2, removed);
        Assert.Null(await documents.GetByIdAsync(first.Id));
        Assert.Null(await documents.GetByIdAsync(second.Id));
    }

    [Fact]
    public async Task A_directory_leaving_cascades_to_the_documents_beneath_it()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Cascade");
        var root = await FolderAsync(db, directory);
        var inner = await FolderAsync(db, $"{directory}/Inner", root);
        var nested = await documents.CreateAsync(
            NewDocument(inner, $"{directory}/Inner", "Nested"));

        // The cascade is the database's, not the repository's — which is why it
        // is worth a test here rather than being assumed.
        db.Folders.Remove(await db.Folders.FindAsync(root) ?? throw new InvalidOperationException());
        await db.SaveChangesAsync();

        Assert.Null(await documents.GetByIdAsync(nested.Id));
        Assert.Null(await folders.GetByIdAsync(inner));
    }

    [Fact]
    public async Task GetStatsAsync_counts_pending_and_indexing_together_as_in_pipeline()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var before = await documents.GetStatsAsync();

        var directory = Unique("Stats");
        var folder = await FolderAsync(db, directory);
        var indexed = await documents.CreateAsync(NewDocument(folder, directory, "Indexed"));
        var failed = await documents.CreateAsync(NewDocument(folder, directory, "Broken"));
        await documents.CreateAsync(NewDocument(folder, directory, "Waiting"));

        await documents.SetStatusAsync(indexed.Id, IngestionStatus.Indexed, chunkCount: 7);
        await documents.SetStatusAsync(failed.Id, IngestionStatus.Failed, "boom");

        var after = await documents.GetStatsAsync();

        Assert.Equal(before.Documents + 3, after.Documents);
        Assert.Equal(before.Indexed + 1, after.Indexed);
        Assert.Equal(before.Failed + 1, after.Failed);
        Assert.Equal(before.InPipeline + 1, after.InPipeline);
        Assert.Equal(before.Chunks + 7, after.Chunks);
    }

    [Fact]
    public async Task GetAllTagsAsync_returns_a_sorted_distinct_set()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var directory = Unique("Tags");
        var folder = await FolderAsync(db, directory);

        var one = await documents.CreateAsync(NewDocument(folder, directory, "One"));
        var two = await documents.CreateAsync(NewDocument(folder, directory, "Two"));

        await documents.UpdateMetadataAsync(
            one.Id, new DocumentMetadataUpdateDto { Tags = ["zeta-tag", "alpha-tag"] });
        await documents.UpdateMetadataAsync(
            two.Id, new DocumentMetadataUpdateDto { Tags = ["alpha-tag"] });

        var tags = await documents.GetAllTagsAsync();

        Assert.Contains("alpha-tag", tags);
        Assert.Contains("zeta-tag", tags);
        Assert.Single(tags, tag => tag == "alpha-tag");
        Assert.True(
            tags.ToList().IndexOf("alpha-tag") < tags.ToList().IndexOf("zeta-tag"),
            "tags should come back sorted");
    }
}
