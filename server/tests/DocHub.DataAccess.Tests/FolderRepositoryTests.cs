using DocHub.DataAccess.Repositories;

namespace DocHub.DataAccess.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class FolderRepositoryTests(PostgresFixture fixture)
{
    private static readonly Guid Owner = DocHubDbContext.SystemUserId;

    /// <summary>Unique per test so tests sharing the database cannot collide.</summary>
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..24];

    [Fact]
    public async Task CreateAsync_builds_the_materialised_path_from_the_parent()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var root = await repository.CreateAsync(null, Unique("Engineering"), Owner);
        var child = await repository.CreateAsync(root.Id, "Onboarding", Owner);
        var grandchild = await repository.CreateAsync(child.Id, "Environment", Owner);

        Assert.Equal(root.Name, root.Path);
        Assert.Equal($"{root.Name}/Onboarding", child.Path);
        Assert.Equal($"{root.Name}/Onboarding/Environment", grandchild.Path);
    }

    [Fact]
    public async Task RenameAsync_rewrites_the_paths_of_the_whole_subtree()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var root = await repository.CreateAsync(null, Unique("Ops"), Owner);
        var child = await repository.CreateAsync(root.Id, "Deployment", Owner);
        var grandchild = await repository.CreateAsync(child.Id, "IIS", Owner);

        var renamed = await repository.RenameAsync(root.Id, "Operations");

        Assert.NotNull(renamed);
        Assert.Equal("Operations", renamed.Path);

        var reloadedChild = await repository.GetByIdAsync(child.Id);
        var reloadedGrandchild = await repository.GetByIdAsync(grandchild.Id);

        Assert.Equal("Operations/Deployment", reloadedChild!.Path);
        Assert.Equal("Operations/Deployment/IIS", reloadedGrandchild!.Path);
    }

    [Fact]
    public async Task GetBreadcrumbAsync_returns_ancestors_root_first()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var root = await repository.CreateAsync(null, Unique("Product"), Owner);
        var child = await repository.CreateAsync(root.Id, "Specs", Owner);
        var leaf = await repository.CreateAsync(child.Id, "Drafts", Owner);

        var breadcrumb = await repository.GetBreadcrumbAsync(leaf.Id);

        Assert.Equal([root.Id, child.Id, leaf.Id], breadcrumb.Select(folder => folder.Id));
    }

    [Fact]
    public async Task GetBreadcrumbAsync_does_not_match_a_folder_sharing_a_name_prefix()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        // "Eng" is a string prefix of "Engineering" — a naive LIKE would treat
        // it as an ancestor. The "/" guard is what prevents that.
        var suffix = Guid.NewGuid().ToString("N")[..6];
        await repository.CreateAsync(null, $"Eng{suffix}", Owner);
        var longer = await repository.CreateAsync(null, $"Eng{suffix}ineering", Owner);

        var breadcrumb = await repository.GetBreadcrumbAsync(longer.Id);

        Assert.Single(breadcrumb);
        Assert.Equal(longer.Id, breadcrumb[0].Id);
    }

    [Fact]
    public async Task NameTakenAsync_only_considers_siblings()
    {
        await using var db = fixture.CreateContext();
        var repository = new FolderRepository(db);

        var rootA = await repository.CreateAsync(null, Unique("A"), Owner);
        var rootB = await repository.CreateAsync(null, Unique("B"), Owner);
        await repository.CreateAsync(rootA.Id, "Shared", Owner);

        Assert.True(await repository.NameTakenAsync(rootA.Id, "Shared"));
        Assert.True(await repository.NameTakenAsync(rootA.Id, "shared"));
        Assert.False(await repository.NameTakenAsync(rootB.Id, "Shared"));
    }
}
