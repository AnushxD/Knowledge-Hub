using DocHub.DataAccess.Entities;
using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// What changes once <see cref="ICurrentUser"/> is a real principal rather than
/// one seeded constant.
///
/// Every service already attributed ownership to <c>currentUser.Id</c>, so
/// nothing here is new code — but nothing ever proved it, because there was
/// only ever one user. With two, "whoever uploaded it owns it" and "your
/// conversations are yours" become claims that can actually fail.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class OwnershipTests(StackFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    private static UploadRequest Upload(string body, string fileName) =>
        new(StackFixture.FileOf(body), fileName, "text/markdown",
            System.Text.Encoding.UTF8.GetByteCount(body));

    /// <summary>
    /// A second real user row. Ownership is a foreign key, so a principal that
    /// does not exist in the table cannot own anything.
    /// </summary>
    private static async Task<Guid> AddUserAsync(StackFixture.Scope scope, string name)
    {
        var id = Guid.CreateVersion7();
        var email = $"{Unique(name).ToLowerInvariant()}@dochub.test";

        scope.Db.Users.Add(new User
        {
            Id = id,
            Name = name,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Role = Roles.Editor,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await scope.Db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task A_document_is_owned_by_whoever_uploaded_it()
    {
        await using var scope = fixture.NewScope();

        var otherId = await AddUserAsync(scope, "Second Person");
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("own")));

        // Upload as the default principal.
        var first = await scope.Documents.UploadAsync(folder.Id, Upload("# One", "one.md"));

        // Then as somebody else, in the same scope — this is what a second
        // request from a different person amounts to.
        scope.User.Id = otherId;
        var second = await scope.Documents.UploadAsync(folder.Id, Upload("# Two", "two.md"));

        Assert.NotEqual(first.Owner.Id, second.Owner.Id);
        Assert.Equal(otherId, second.Owner.Id);
        Assert.Equal("Second Person", second.Owner.Name);
    }

    [Fact]
    public async Task A_folder_is_owned_by_whoever_created_it()
    {
        await using var scope = fixture.NewScope();
        var otherId = await AddUserAsync(scope, "Folder Maker");

        scope.User.Id = otherId;
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("fown")));

        var stored = await scope.Db.Folders.FindAsync(folder.Id);
        Assert.Equal(otherId, stored!.OwnerId);
    }

    [Fact]
    public async Task Conversations_are_listed_only_for_the_person_who_had_them()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, "chatown");

        // One question as the default principal.
        await DrainAsync(scope, new AskRequest
        {
            Question = "How do I restart the ingestion worker?",
            FolderId = folderId,
        });

        var mine = await scope.Chat.ListSessionsAsync();
        Assert.NotEmpty(mine);

        // Someone else must not see it. A transcript can quote any document the
        // asker could reach, so another person's history is a way to read
        // content second-hand.
        scope.User.Id = await AddUserAsync(scope, "Nosy Colleague");

        var theirs = await scope.Chat.ListSessionsAsync();
        Assert.Empty(theirs);
    }

    private const string RunbookBody = """
        ## Restarting the ingestion worker

        Drain the queue before restarting the worker, then bring it back with the
        supervisor. Jobs already in flight finish; nothing is lost.
        """;

    private static async Task<Guid> IndexAsync(StackFixture.Scope scope, string name)
    {
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique(name)));
        var document = await scope.Documents.UploadAsync(
            folder.Id, Upload(RunbookBody, $"{name}.md"));

        await scope.Ingestion.IngestAsync(document.Id);
        return folder.Id;
    }

    private static async Task DrainAsync(StackFixture.Scope scope, AskRequest request)
    {
        await foreach (var _ in scope.Chat.AskAsync(request)) { }
    }
}
