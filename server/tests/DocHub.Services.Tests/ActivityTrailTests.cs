using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// The activity trail, recorded as a side effect of ordinary operations.
///
/// Written against the services rather than the log directly: the value is not
/// that <c>RecordAsync</c> works, it is that syncing, editing and indexing
/// actually call it. A test that exercised the log alone would keep passing
/// after somebody removed the call that matters.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class ActivityTrailTests(StackFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    [Fact]
    public async Task A_file_appearing_in_the_repository_is_recorded_against_nobody()
    {
        await using var scope = fixture.NewScope();

        var document = await scope.PublishAsync($"{Unique("act")}/hello.md", "# Hello");

        var recent = await scope.Activity.RecentAsync(20);
        var entry = Assert.Single(recent, e => e.Type == "added" && e.TargetId == document.Id);

        Assert.Equal(document.Title, entry.Target);

        // Nobody added it — the repository did. Naming the signed-in caller
        // would credit them with a commit they may not have written, and
        // naming the seeded administrator would be worse.
        Assert.Null(entry.Actor);
    }

    [Fact]
    public async Task A_manual_sync_is_recorded_against_the_person_who_asked_for_it()
    {
        await using var scope = fixture.NewScope();

        scope.Repository.Put($"{Unique("who")}/hello.md", "# Hello");
        await scope.Mirror.SyncAsync(scope.User.Id);

        // The newest, not the only one: every test in this collection syncs
        // against the same database, so the feed is full of them.
        var recent = await scope.Activity.RecentAsync(20);
        var entry = recent.First(e => e.Type == "synced");

        // The one kind of entry in this feed with an actor: somebody pressed
        // the button.
        Assert.Equal(scope.User.Id, entry.Actor?.Id);
    }

    [Fact]
    public async Task Indexing_is_recorded_without_borrowing_an_identity()
    {
        await using var scope = fixture.NewScope();

        var document = await scope.PublishIndexedAsync(
            $"{Unique("idx")}/runbook.md", "# Runbook\n\nDrain the queue first.");

        var recent = await scope.Activity.RecentAsync(20);
        var entry = Assert.Single(recent, e => e.Type == "indexed" && e.TargetId == document.Id);

        // Ingestion runs unattended. With no owner left to attribute it to, the
        // honest answer is no actor rather than the nearest available account.
        Assert.Null(entry.Actor);
    }

    [Fact]
    public async Task A_file_leaving_the_repository_leaves_a_record_of_having_gone()
    {
        await using var scope = fixture.NewScope();

        var path = $"{Unique("del")}/gone.md";
        var document = await scope.PublishAsync(path, "# Gone");

        scope.Repository.Remove(path);
        await scope.Mirror.SyncAsync(actorId: null);

        var recent = await scope.Activity.RecentAsync(20);
        var entry = Assert.Single(recent, e => e.Type == "removed" && e.Target == document.Title);

        // The name survives the row it described — the whole point of
        // denormalising it, and the entry most likely to be asked about.
        Assert.Equal(document.Title, entry.Target);

        // No link: the document is gone, and one would only lead to "not found".
        Assert.Null(entry.TargetId);
    }

    [Fact]
    public async Task Starring_a_document_does_not_clutter_the_feed()
    {
        await using var scope = fixture.NewScope();

        var document = await scope.PublishAsync($"{Unique("star")}/star.md", "# Star");

        await scope.Documents.UpdateAsync(
            document.Id, new UpdateDocumentRequest(null, null, null, IsStarred: true));

        var recent = await scope.Activity.RecentAsync(20);

        // A bookmark is not an edit. Recorded, every star and un-star would
        // push real changes out of a feed that shows the most recent dozen.
        Assert.DoesNotContain(recent, e => e.Type == "updated" && e.TargetId == document.Id);

        // A real edit still is, and it is the one thing in this feed a person
        // genuinely did to a document, so it keeps their name.
        await scope.Documents.UpdateAsync(
            document.Id, new UpdateDocumentRequest("Renamed", null, null, null));

        recent = await scope.Activity.RecentAsync(20);
        var edit = Assert.Single(recent, e => e.Type == "updated" && e.TargetId == document.Id);
        Assert.Equal(scope.User.Id, edit.Actor?.Id);
    }

    [Fact]
    public async Task The_feed_is_newest_first()
    {
        await using var scope = fixture.NewScope();

        var directory = Unique("ord");
        await scope.PublishAsync($"{directory}/one.md", "# One");
        await scope.PublishAsync($"{directory}/two.md", "# Two");

        var recent = await scope.Activity.RecentAsync(20);

        Assert.Equal(
            recent.Select(e => e.At).OrderByDescending(at => at),
            recent.Select(e => e.At));
    }
}
