using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

[Collection(nameof(StackCollection))]
public sealed class DocumentWorkflowTests(StackFixture fixture)
{
    private static UploadRequest Upload(string body, string fileName, string? note = null) =>
        new(StackFixture.FileOf(body), fileName, "text/markdown",
            System.Text.Encoding.UTF8.GetByteCount(body), note);

    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    [Fact]
    public async Task Uploading_persists_the_row_and_the_file_together()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Docs")));

        var created = await scope.Documents.UploadAsync(
            folder.Id, Upload("# Setup", "setup.md"));

        Assert.Equal("setup", created.Title);
        Assert.Equal("pending", created.Status);
        Assert.Equal(1, created.Version);

        // The file must actually be readable back through the service.
        var content = await scope.Documents.DownloadAsync(created.Id);
        using var reader = new StreamReader(content.Content);
        Assert.Equal("# Setup", await reader.ReadToEndAsync());
        Assert.Equal("setup.md", content.FileName);
    }

    [Fact]
    public async Task Adding_a_version_keeps_the_previous_file_retrievable()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Ver")));
        var created = await scope.Documents.UploadAsync(folder.Id, Upload("v1 body", "notes.md"));

        var beforePath = await StoragePathOf(scope, created.Id);

        var updated = await scope.Documents.AddVersionAsync(
            created.Id, Upload("v2 body", "notes.md", "Reviewed"));

        Assert.Equal(2, updated.Version);

        // Version history is only meaningful if the old blob survives — the
        // DocumentVersion row still points at it.
        Assert.True(await fixture.Storage.ExistsAsync(beforePath));

        var current = await scope.Documents.DownloadAsync(created.Id);
        using var reader = new StreamReader(current.Content);
        Assert.Equal("v2 body", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Deleting_a_document_frees_every_version_it_owned()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Del")));
        var created = await scope.Documents.UploadAsync(folder.Id, Upload("one", "a.md"));

        var firstPath = await StoragePathOf(scope, created.Id);
        await scope.Documents.AddVersionAsync(created.Id, Upload("two", "a.md"));
        var secondPath = await StoragePathOf(scope, created.Id);

        await scope.Documents.DeleteAsync(created.Id);

        Assert.False(await fixture.Storage.ExistsAsync(firstPath));
        Assert.False(await fixture.Storage.ExistsAsync(secondPath));
        await Assert.ThrowsAsync<NotFoundException>(() => scope.Documents.GetAsync(created.Id));
    }

    [Fact]
    public async Task Deleting_a_folder_frees_the_files_of_documents_beneath_it()
    {
        await using var scope = fixture.NewScope();
        var root = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Tree")));
        var child = await scope.Folders.CreateAsync(new CreateFolderRequest(root.Id, "Nested"));

        var nested = await scope.Documents.UploadAsync(child.Id, Upload("deep", "deep.md"));
        var nestedPath = await StoragePathOf(scope, nested.Id);

        await scope.Folders.DeleteAsync(root.Id);

        // This is the seam a mocked repository or storage would never catch:
        // the cascade has to hand its blob paths to storage for cleanup.
        Assert.False(await fixture.Storage.ExistsAsync(nestedPath));
        await Assert.ThrowsAsync<NotFoundException>(() => scope.Documents.GetAsync(nested.Id));
    }

    [Fact]
    public async Task Uploading_to_a_missing_folder_is_rejected_before_the_file_is_stored()
    {
        await using var scope = fixture.NewScope();
        var content = StackFixture.FileOf("body");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            scope.Documents.UploadAsync(
                Guid.NewGuid(),
                new UploadRequest(content, "x.md", "text/markdown", 4)));

        // An untouched stream proves the upload was rejected before anything
        // reached storage, so no orphaned blob can have been created.
        Assert.Equal(0, content.Position);
    }

    [Theory]
    [InlineData("installer.exe", ".exe files are not allowed.")]
    [InlineData("script.SH", ".sh files are not allowed.")]
    [InlineData("noextension", "The file must have an extension.")]
    public async Task Uploads_are_rejected_by_file_type(string fileName, string expected)
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Bad")));

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.Documents.UploadAsync(folder.Id, Upload("payload", fileName)));

        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public async Task Upload_strips_any_directory_component_from_the_file_name()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Path")));

        var created = await scope.Documents.UploadAsync(
            folder.Id, Upload("payload", "../../../etc/passwd.md"));

        Assert.Equal("passwd.md", created.FileName);
        Assert.DoesNotContain("..", created.FileName);
    }

    [Fact]
    public async Task Empty_uploads_are_rejected()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Empty")));

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.Documents.UploadAsync(
                folder.Id, new UploadRequest(Stream.Null, "empty.md", "text/markdown", 0)));

        Assert.Equal("The uploaded file is empty.", error.Message);
    }

    [Fact]
    public async Task Updating_metadata_normalises_tags()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Tag")));
        var created = await scope.Documents.UploadAsync(folder.Id, Upload("body", "t.md"));

        var updated = await scope.Documents.UpdateAsync(
            created.Id,
            new UpdateDocumentRequest(
                Title: "  Renamed  ",
                Description: null,
                Tags: ["#Setup", "setup", " Docker ", ""],
                IsStarred: true));

        Assert.Equal("Renamed", updated.Title);
        Assert.Equal(["setup", "docker"], updated.Tags);
        Assert.True(updated.IsStarred);
    }

    [Fact]
    public async Task Queries_scope_to_a_folder_and_its_descendants()
    {
        await using var scope = fixture.NewScope();
        var root = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Scope")));
        var child = await scope.Folders.CreateAsync(new CreateFolderRequest(root.Id, "Inner"));

        await scope.Documents.UploadAsync(root.Id, Upload("a", "a.md"));
        await scope.Documents.UploadAsync(child.Id, Upload("b", "b.md"));

        var recursive = await scope.Documents.QueryAsync(
            new DocumentQueryRequest { FolderId = root.Id });
        var direct = await scope.Documents.QueryAsync(
            new DocumentQueryRequest { FolderId = root.Id, Recursive = false });

        Assert.Equal(2, recursive.Count);
        Assert.Single(direct);
    }

    private static async Task<string> StoragePathOf(StackFixture.Scope scope, Guid documentId)
    {
        var repository = new DataAccess.Repositories.DocumentRepository(scope.Db);
        return (await repository.GetStoragePathAsync(documentId))!;
    }
}
