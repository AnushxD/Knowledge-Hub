using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;

namespace DocHub.DataAccess.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class DocumentRepositoryTests(PostgresFixture fixture)
{
    private static readonly Guid Owner = DocHubDbContext.SystemUserId;

    private static NewDocumentDto NewDocument(Guid folderId, string title, params string[] tags) => new()
    {
        FolderId = folderId,
        Title = title,
        FileName = $"{title.ToLowerInvariant()}.md",
        Extension = "md",
        ContentType = "text/markdown",
        SizeBytes = 1024,
        StoragePath = $"documents/{Guid.NewGuid():N}.md",
        OwnerId = Owner,
        Tags = tags,
    };

    [Fact]
    public async Task CreateAsync_starts_pending_with_version_one_and_a_history_row()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var folder = await folders.CreateAsync(null, $"Docs-{Guid.NewGuid():N}"[..20], Owner);
        var created = await documents.CreateAsync(NewDocument(folder.Id, "Runbook"));

        Assert.Equal(IngestionStatus.Pending, created.Status);
        Assert.Equal(1, created.Version);
        Assert.Null(created.ChunkCount);

        var detail = await documents.GetByIdAsync(created.Id);
        Assert.NotNull(detail);
        Assert.Single(detail.Versions);
        Assert.Equal("Initial upload", detail.Versions[0].Note);
        Assert.Equal("Local Developer", detail.Document.Owner.Name);
    }

    [Fact]
    public async Task QueryAsync_with_recursive_scope_includes_descendant_folders()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var root = await folders.CreateAsync(null, $"Tree-{Guid.NewGuid():N}"[..20], Owner);
        var child = await folders.CreateAsync(root.Id, "Nested", Owner);

        await documents.CreateAsync(NewDocument(root.Id, "AtRoot"));
        await documents.CreateAsync(NewDocument(child.Id, "AtChild"));

        var recursive = await documents.QueryAsync(new DocumentQueryDto { FolderId = root.Id });
        var direct = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = root.Id, Recursive = false });

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

        var folder = await folders.CreateAsync(null, $"Filter-{Guid.NewGuid():N}"[..20], Owner);
        var postgres = await documents.CreateAsync(
            NewDocument(folder.Id, "PostgresSetup", "database", "setup"));
        await documents.CreateAsync(NewDocument(folder.Id, "HangfireTriage", "runbook"));

        await documents.SetStatusAsync(postgres.Id, IngestionStatus.Indexed, chunkCount: 12);

        var byTag = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = folder.Id, Tags = ["database"] });
        var byStatus = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = folder.Id, Statuses = [IngestionStatus.Indexed] });
        // Deliberately different casing — filtering must be case-insensitive.
        var byText = await documents.QueryAsync(
            new DocumentQueryDto { FolderId = folder.Id, Text = "postgres" });

        Assert.Equal("PostgresSetup", Assert.Single(byTag).Title);
        Assert.Equal(12, Assert.Single(byStatus).ChunkCount);
        Assert.Equal("PostgresSetup", Assert.Single(byText).Title);
    }

    [Fact]
    public async Task AddVersionAsync_bumps_the_version_and_sends_the_document_back_for_ingestion()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var folder = await folders.CreateAsync(null, $"Ver-{Guid.NewGuid():N}"[..20], Owner);
        var created = await documents.CreateAsync(NewDocument(folder.Id, "Policy"));
        await documents.SetStatusAsync(created.Id, IngestionStatus.Indexed, chunkCount: 9);

        var updated = await documents.AddVersionAsync(
            created.Id, "documents/v2.md", 2048, "Reviewed", Owner);

        Assert.NotNull(updated);
        Assert.Equal(2, updated.Version);
        Assert.Equal(2048, updated.SizeBytes);
        // New content invalidates the old chunks, so it must re-ingest.
        Assert.Equal(IngestionStatus.Pending, updated.Status);
        Assert.Null(updated.ChunkCount);

        var detail = await documents.GetByIdAsync(created.Id);
        Assert.Equal(2, detail!.Versions.Count);
        Assert.Equal(2, detail.Versions[0].VersionNumber);
    }

    [Fact]
    public async Task SetStatusAsync_keeps_a_failure_reason_only_while_failed()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var folder = await folders.CreateAsync(null, $"Fail-{Guid.NewGuid():N}"[..20], Owner);
        var created = await documents.CreateAsync(NewDocument(folder.Id, "Scanned"));

        var failed = await documents.SetStatusAsync(
            created.Id, IngestionStatus.Failed, "No extractable text.");
        Assert.Equal("No extractable text.", failed!.FailureReason);

        var retried = await documents.SetStatusAsync(created.Id, IngestionStatus.Indexing);
        Assert.Null(retried!.FailureReason);
    }

    [Fact]
    public async Task DeleteAsync_returns_every_blob_path_the_document_owned()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var folder = await folders.CreateAsync(null, $"Del-{Guid.NewGuid():N}"[..20], Owner);
        var created = await documents.CreateAsync(NewDocument(folder.Id, "Temp"));
        await documents.AddVersionAsync(created.Id, "documents/temp-v2.md", 4096, null, Owner);

        var blobs = await documents.DeleteAsync(created.Id);

        // Both revisions must come back, or the files are orphaned in storage.
        Assert.Equal(2, blobs.Count);
        Assert.Contains("documents/temp-v2.md", blobs);
        Assert.Null(await documents.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_on_a_folder_cascades_and_frees_its_documents_blobs()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var root = await folders.CreateAsync(null, $"Cascade-{Guid.NewGuid():N}"[..20], Owner);
        var child = await folders.CreateAsync(root.Id, "Inner", Owner);
        var nested = await documents.CreateAsync(NewDocument(child.Id, "Nested"));

        var blobs = await folders.DeleteAsync(root.Id);

        Assert.NotEmpty(blobs);
        Assert.Null(await documents.GetByIdAsync(nested.Id));
        Assert.Null(await folders.GetByIdAsync(child.Id));
    }

    [Fact]
    public async Task GetStatsAsync_counts_pending_and_indexing_together_as_in_pipeline()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);

        var before = await documents.GetStatsAsync();

        var folder = await folders.CreateAsync(null, $"Stats-{Guid.NewGuid():N}"[..20], Owner);
        var indexed = await documents.CreateAsync(NewDocument(folder.Id, "Indexed"));
        var failed = await documents.CreateAsync(NewDocument(folder.Id, "Broken"));
        await documents.CreateAsync(NewDocument(folder.Id, "Waiting"));

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

        var folder = await folders.CreateAsync(null, $"Tags-{Guid.NewGuid():N}"[..20], Owner);
        await documents.CreateAsync(NewDocument(folder.Id, "One", "zeta-tag", "alpha-tag"));
        await documents.CreateAsync(NewDocument(folder.Id, "Two", "alpha-tag"));

        var tags = await documents.GetAllTagsAsync();

        Assert.Contains("alpha-tag", tags);
        Assert.Contains("zeta-tag", tags);
        Assert.Single(tags, tag => tag == "alpha-tag");
        Assert.True(
            tags.ToList().IndexOf("alpha-tag") < tags.ToList().IndexOf("zeta-tag"),
            "tags should come back sorted");
    }
}
