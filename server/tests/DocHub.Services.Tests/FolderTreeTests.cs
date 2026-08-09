using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// The folder tree, which is no longer anybody's to make: it is the
/// repository's directory structure, reproduced.
///
/// What is worth testing has moved with it. There are no name rules to enforce
/// — a file system already guarantees them — and the questions that replace
/// them are whether nesting is reproduced faithfully, whether a directory
/// leaving takes its subtree, and whether counts still mean what the sidebar
/// says they mean.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class FolderTreeTests(StackFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    [Fact]
    public async Task Nesting_in_the_repository_is_reproduced_level_by_level()
    {
        await using var scope = fixture.NewScope();
        var directory = Unique("Deep");

        var document = await scope.PublishAsync($"{directory}/one/two/three/file.md", "body");

        var all = await scope.Folders.GetAllAsync();
        var leaf = all.Single(folder => folder.Id == document.FolderId);

        Assert.Equal($"docs/{directory}/one/two/three", leaf.Path);

        // Every intermediate level exists as its own row, including ones the
        // repository never listed on their own — a tree that skipped a level
        // would leave the sidebar unable to draw the branch.
        Assert.Contains(all, folder => folder.Path == $"docs/{directory}/one");
        Assert.Contains(all, folder => folder.Path == $"docs/{directory}/one/two");
    }

    [Fact]
    public async Task The_same_directory_name_is_allowed_under_different_parents()
    {
        await using var scope = fixture.NewScope();
        var a = Unique("A");
        var b = Unique("B");

        var first = await scope.PublishAsync($"{a}/shared/note.md", "a");
        var second = await scope.PublishAsync($"{b}/shared/note.md", "b");

        Assert.NotEqual(first.FolderId, second.FolderId);

        var all = await scope.Folders.GetAllAsync();
        Assert.Equal("shared", all.Single(folder => folder.Id == second.FolderId).Name);
    }

    [Fact]
    public async Task A_directory_leaving_the_repository_takes_its_subtree()
    {
        await using var scope = fixture.NewScope();
        var directory = Unique("Tree");

        var nested = await scope.PublishAsync($"{directory}/nested/deep.md", "deep");
        var elsewhere = await scope.PublishAsync($"{Unique("Other")}/kept.md", "kept");

        scope.Repository.Remove($"{directory}/nested/deep.md");
        await scope.Mirror.SyncAsync(actorId: null);

        var all = await scope.Folders.GetAllAsync();

        Assert.DoesNotContain(all, folder => folder.Path.StartsWith($"docs/{directory}"));
        await Assert.ThrowsAsync<NotFoundException>(() => scope.Documents.GetAsync(nested.Id));

        // And nothing else went with it. A reconciliation that deleted by
        // prefix rather than by exact path would have taken the neighbour too.
        Assert.Contains(all, folder => folder.Id == elsewhere.FolderId);
    }

    [Fact]
    public async Task Document_counts_are_recursive()
    {
        await using var scope = fixture.NewScope();
        var directory = Unique("Count");

        var deep = await scope.PublishAsync($"{directory}/inner/deep.md", "body");

        var all = await scope.Folders.GetAllAsync();
        var root = all.Single(folder => folder.Path == $"docs/{directory}");

        // The sidebar shows everything beneath a folder, not just its direct
        // children.
        Assert.Equal(1, root.DocumentCount);
        Assert.Equal(1, all.Single(folder => folder.Id == deep.FolderId).DocumentCount);
    }
}
