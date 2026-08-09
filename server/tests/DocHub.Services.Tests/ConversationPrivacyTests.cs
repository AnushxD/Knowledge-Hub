using DocHub.DataAccess.Entities;
using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// What still belongs to a person now that documents do not.
///
/// Ownership of documents and folders is gone with uploads — the repository
/// owns them, and there is no "whoever uploaded it" left to be. Conversations
/// are the thing a user still genuinely has, and they are the one thing here
/// that leaks content if the principal is ignored: a transcript quotes whatever
/// the asker could reach, so another person's history is a way to read it
/// second-hand.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class ConversationPrivacyTests(StackFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    private const string RunbookBody = """
        ## Restarting the ingestion worker

        Drain the queue before restarting the worker, then bring it back with the
        supervisor. Jobs already in flight finish; nothing is lost.
        """;

    /// <summary>
    /// A second real user row. A session's user is a foreign key, so a
    /// principal that does not exist in the table cannot have one.
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
    public async Task Conversations_are_listed_only_for_the_person_who_had_them()
    {
        await using var scope = fixture.NewScope();

        var document = await scope.PublishIndexedAsync(
            $"{Unique("chatown")}/runbook.md", RunbookBody);

        // One question as the default principal.
        await DrainAsync(scope, new AskRequest
        {
            Question = "How do I restart the ingestion worker?",
            FolderId = document.FolderId,
        });

        var mine = await scope.Chat.ListSessionsAsync();
        Assert.NotEmpty(mine);

        // Someone else must not see it.
        scope.User.Id = await AddUserAsync(scope, "Nosy Colleague");

        var theirs = await scope.Chat.ListSessionsAsync();
        Assert.Empty(theirs);
    }

    [Fact]
    public async Task A_metadata_edit_is_attributed_to_whoever_made_it()
    {
        await using var scope = fixture.NewScope();

        var document = await scope.PublishAsync($"{Unique("edit")}/guide.md", "# Guide");
        var otherId = await AddUserAsync(scope, "Second Person");

        // In the same scope, which is what a second request from a different
        // person amounts to.
        scope.User.Id = otherId;
        await scope.Documents.UpdateAsync(
            document.Id, new UpdateDocumentRequest("Renamed", null, null, null));

        var recent = await scope.Activity.RecentAsync(20);
        var entry = Assert.Single(recent, e => e.Type == "updated" && e.TargetId == document.Id);

        Assert.Equal(otherId, entry.Actor?.Id);
        Assert.Equal("Second Person", entry.Actor?.Name);
    }

    private static async Task DrainAsync(StackFixture.Scope scope, AskRequest request)
    {
        await foreach (var _ in scope.Chat.AskAsync(request)) { }
    }
}
