using DocHub.DataAccess.Repositories;

namespace DocHub.DataAccess.Tests;

/// <summary>
/// Reconciling the folder table against the repository's directories.
///
/// <c>ReconcileAsync</c> is authoritative over the whole tree — anything absent
/// from its list is taken to have left the repository — so these tests each
/// hand it the complete set they expect to exist afterwards, and assert on
/// their own directories rather than on the size of the table.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FolderRepositoryTests(PostgresFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Reconciling_creates_every_intermediate_level()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var root = Unique("Deep");
        var map = await repository.ReconcileAsync([$"{root}/one/two/three"]);

        // GitLab lists "a/b/c" as a tree entry; a repository whose only file is
        // deep inside never names the levels above it on their own. Without
        // filling them in, the sidebar cannot draw the branch.
        Assert.Equal(
            [root, $"{root}/one", $"{root}/one/two", $"{root}/one/two/three"],
            map.Keys.Where(path => path.StartsWith(root, StringComparison.Ordinal))
                .OrderBy(path => path.Length));

        var stored = await repository.GetByIdAsync(map[$"{root}/one/two"]);
        Assert.Equal("two", stored!.Name);
        Assert.Equal(map[$"{root}/one"], stored.ParentId);
    }

    [Fact]
    public async Task Reconciling_twice_changes_nothing()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var root = Unique("Idem");
        var first = await repository.ReconcileAsync([$"{root}/child"]);
        var second = await repository.ReconcileAsync([$"{root}/child"]);

        // Every sync reconciles. If this were not idempotent, a folder's id
        // would change under the documents pointing at it.
        Assert.Equal(first[$"{root}/child"], second[$"{root}/child"]);
    }

    [Fact]
    public async Task A_directory_absent_from_the_tree_is_removed()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var root = Unique("Prune");
        var before = await repository.ReconcileAsync([$"{root}/kept", $"{root}/gone"]);

        await repository.ReconcileAsync([$"{root}/kept"]);

        Assert.NotNull(await repository.GetByIdAsync(before[$"{root}/kept"]));
        Assert.Null(await repository.GetByIdAsync(before[$"{root}/gone"]));
    }

    [Fact]
    public async Task The_same_name_is_allowed_under_different_parents()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var a = Unique("A");
        var b = Unique("B");
        var map = await repository.ReconcileAsync([$"{a}/shared", $"{b}/shared"]);

        Assert.NotEqual(map[$"{a}/shared"], map[$"{b}/shared"]);
    }

    [Fact]
    public async Task GetBreadcrumbAsync_returns_ancestors_root_first()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var root = Unique("Crumb");
        var map = await repository.ReconcileAsync([$"{root}/child/leaf"]);

        var breadcrumb = await repository.GetBreadcrumbAsync(map[$"{root}/child/leaf"]);

        Assert.Equal(
            [map[root], map[$"{root}/child"], map[$"{root}/child/leaf"]],
            breadcrumb.Select(folder => folder.Id));
    }

    [Fact]
    public async Task GetBreadcrumbAsync_does_not_match_a_folder_sharing_a_name_prefix()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        // "Eng" is a string prefix of "Engineering" — a naive LIKE would treat
        // it as an ancestor. The "/" guard is what prevents that.
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var map = await repository.ReconcileAsync([$"Eng{suffix}", $"Eng{suffix}ineering"]);

        var breadcrumb = await repository.GetBreadcrumbAsync(map[$"Eng{suffix}ineering"]);

        Assert.Single(breadcrumb);
        Assert.Equal(map[$"Eng{suffix}ineering"], breadcrumb[0].Id);
    }
}
