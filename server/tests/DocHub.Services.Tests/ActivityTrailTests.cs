using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// The activity trail, recorded as a side effect of ordinary operations.
///
/// Written against the services rather than the log directly: the value is not
/// that <c>RecordAsync</c> works, it is that uploading, deleting and indexing
/// actually call it. A test that exercised the log alone would keep passing
/// after somebody removed the call that matters.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class ActivityTrailTests(StackFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    private static UploadRequest Upload(string body, string fileName) =>
        new(StackFixture.FileOf(body), fileName, "text/markdown",
            System.Text.Encoding.UTF8.GetByteCount(body));

    [Fact]
    public async Task Uploading_a_document_is_recorded_against_the_person_who_did_it()
    {
        await using var scope = fixture.NewScope();

        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("act")));
        var document = await scope.Documents.UploadAsync(folder.Id, Upload("# Hello", "hello.md"));

        var recent = await scope.Activity.RecentAsync(20);
        var entry = Assert.Single(recent, e => e.Type == "uploaded" && e.TargetId == document.Id);

        Assert.Equal(scope.User.Id, entry.Actor.Id);
        Assert.Equal(document.Title, entry.Target);

        // The folder that held it is in there too.
        Assert.Contains(recent, e => e.Type == "folder-created" && e.Target == folder.Name);
    }

    [Fact]
    public async Task Indexing_is_recorded_against_the_owner_rather_than_nobody()
    {
        await using var scope = fixture.NewScope();

        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("idx")));
        var document = await scope.Documents.UploadAsync(
            folder.Id, Upload("# Runbook\n\nDrain the queue first.", "runbook.md"));

        await scope.Ingestion.IngestAsync(document.Id);

        var recent = await scope.Activity.RecentAsync(20);
        var entry = Assert.Single(recent, e => e.Type == "indexed" && e.TargetId == document.Id);

        // Ingestion runs unattended, so the entry has to borrow an identity.
        // The owner is the only honest one available.
        Assert.Equal(scope.User.Id, entry.Actor.Id);
    }

    [Fact]
    public async Task A_deleted_document_leaves_a_record_of_having_been_deleted()
    {
        await using var scope = fixture.NewScope();

        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("del")));
        var document = await scope.Documents.UploadAsync(folder.Id, Upload("# Gone", "gone.md"));

        await scope.Documents.DeleteAsync(document.Id);

        var recent = await scope.Activity.RecentAsync(20);
        var entry = Assert.Single(recent, e => e.Type == "deleted" && e.Target == document.Title);

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

        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("star")));
        var document = await scope.Documents.UploadAsync(folder.Id, Upload("# Star", "star.md"));

        await scope.Documents.UpdateAsync(
            document.Id, new UpdateDocumentRequest(null, null, null, IsStarred: true));

        var recent = await scope.Activity.RecentAsync(20);

        // A bookmark is not an edit. Recorded, every star and un-star would
        // push real changes out of a feed that shows the most recent dozen.
        Assert.DoesNotContain(recent, e => e.Type == "updated" && e.TargetId == document.Id);

        // A real edit still is.
        await scope.Documents.UpdateAsync(
            document.Id, new UpdateDocumentRequest("Renamed", null, null, null));

        recent = await scope.Activity.RecentAsync(20);
        Assert.Contains(recent, e => e.Type == "updated" && e.TargetId == document.Id);
    }

    [Fact]
    public async Task The_feed_is_newest_first()
    {
        await using var scope = fixture.NewScope();

        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("ord")));
        await scope.Documents.UploadAsync(folder.Id, Upload("# One", "one.md"));
        await scope.Documents.UploadAsync(folder.Id, Upload("# Two", "two.md"));

        var recent = await scope.Activity.RecentAsync(20);

        Assert.Equal(
            recent.Select(e => e.At).OrderByDescending(at => at),
            recent.Select(e => e.At));
    }
}
