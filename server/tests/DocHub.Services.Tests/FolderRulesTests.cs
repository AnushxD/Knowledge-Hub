using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

[Collection(nameof(StackCollection))]
public sealed class FolderRulesTests(StackFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    [Fact]
    public async Task Sibling_names_must_be_unique_regardless_of_case()
    {
        await using var scope = fixture.NewScope();
        var name = Unique("Dup");

        await scope.Folders.CreateAsync(new CreateFolderRequest(null, name));

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.Folders.CreateAsync(new CreateFolderRequest(null, name.ToUpperInvariant())));

        Assert.Contains("already exists", error.Message);
    }

    [Fact]
    public async Task The_same_name_is_allowed_under_different_parents()
    {
        await using var scope = fixture.NewScope();
        var a = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("A")));
        var b = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("B")));

        await scope.Folders.CreateAsync(new CreateFolderRequest(a.Id, "Shared"));
        var second = await scope.Folders.CreateAsync(new CreateFolderRequest(b.Id, "Shared"));

        Assert.Equal("Shared", second.Name);
    }

    [Fact]
    public async Task A_slash_in_a_name_is_rejected()
    {
        await using var scope = fixture.NewScope();

        // "/" separates segments in the materialised path, so allowing it would
        // corrupt every descendant path.
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.Folders.CreateAsync(new CreateFolderRequest(null, "Eng/Onboarding")));

        Assert.Equal("Folder name cannot contain '/'.", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_rejected(string name)
    {
        await using var scope = fixture.NewScope();

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.Folders.CreateAsync(new CreateFolderRequest(null, name)));

        Assert.Equal("Folder name is required.", error.Message);
    }

    [Fact]
    public async Task Creating_under_a_missing_parent_is_a_not_found()
    {
        await using var scope = fixture.NewScope();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            scope.Folders.CreateAsync(new CreateFolderRequest(Guid.NewGuid(), "Orphan")));
    }

    [Fact]
    public async Task Renaming_rewrites_descendant_paths()
    {
        await using var scope = fixture.NewScope();
        var root = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Old")));
        var child = await scope.Folders.CreateAsync(new CreateFolderRequest(root.Id, "Child"));

        var newName = Unique("New");
        await scope.Folders.RenameAsync(root.Id, new RenameFolderRequest(newName));

        var all = await scope.Folders.GetAllAsync();
        var reloaded = all.Single(folder => folder.Id == child.Id);

        Assert.Equal($"{newName}/Child", reloaded.Path);
    }

    [Fact]
    public async Task Document_counts_are_recursive()
    {
        await using var scope = fixture.NewScope();
        var root = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Count")));
        var child = await scope.Folders.CreateAsync(new CreateFolderRequest(root.Id, "Inner"));

        await scope.Documents.UploadAsync(
            child.Id,
            new UploadRequest(StackFixture.FileOf("body"), "deep.md", "text/markdown", 4));

        var all = await scope.Folders.GetAllAsync();

        // The sidebar shows everything beneath a folder, not just its direct
        // children.
        Assert.Equal(1, all.Single(folder => folder.Id == root.Id).DocumentCount);
        Assert.Equal(1, all.Single(folder => folder.Id == child.Id).DocumentCount);
    }
}
