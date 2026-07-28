using System.Text;

namespace DocHub.Integrations.Tests;

[Collection(nameof(AzuriteCollection))]
public sealed class AzureBlobFileStorageTests(AzuriteFixture fixture)
{
    private static async Task<string> ReadAllAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task SaveAsync_then_OpenReadAsync_returns_the_same_content_and_type()
    {
        const string body = "# Dev Environment Setup\n\nRun `docker compose up -d`.";

        var path = await fixture.Storage.SaveAsync(
            AzuriteFixture.StreamOf(body), "setup.md", "text/markdown");

        await using var file = await fixture.Storage.OpenReadAsync(path);

        Assert.NotNull(file);
        Assert.Equal("text/markdown", file.ContentType);
        Assert.Equal(Encoding.UTF8.GetByteCount(body), file.SizeBytes);
        Assert.Equal(body, await ReadAllAsync(file.Content));
    }

    [Fact]
    public async Task SaveAsync_generates_a_date_partitioned_path_and_keeps_the_extension()
    {
        var path = await fixture.Storage.SaveAsync(
            AzuriteFixture.StreamOf("x"), "Quarterly Report.PDF", "application/pdf");

        var now = DateTimeOffset.UtcNow;
        Assert.StartsWith($"{now:yyyy}/{now:MM}/", path);
        Assert.EndsWith(".pdf", path);
    }

    [Fact]
    public async Task SaveAsync_never_lets_a_crafted_filename_shape_the_path()
    {
        // A traversal attempt must not escape the container or plant a blob at
        // an attacker-chosen location — only the extension is ever taken from
        // the supplied name.
        var path = await fixture.Storage.SaveAsync(
            AzuriteFixture.StreamOf("payload"),
            "../../../etc/passwd",
            "application/octet-stream");

        Assert.DoesNotContain("..", path);
        Assert.DoesNotContain("etc", path);
        Assert.DoesNotContain("passwd", path);

        var now = DateTimeOffset.UtcNow;
        Assert.StartsWith($"{now:yyyy}/{now:MM}/", path);
    }

    [Fact]
    public async Task SaveAsync_gives_every_upload_its_own_path_even_for_identical_names()
    {
        var first = await fixture.Storage.SaveAsync(
            AzuriteFixture.StreamOf("one"), "notes.md", "text/markdown");
        var second = await fixture.Storage.SaveAsync(
            AzuriteFixture.StreamOf("two"), "notes.md", "text/markdown");

        Assert.NotEqual(first, second);

        // Version history depends on this: a re-upload must never overwrite the
        // blob an older DocumentVersion row still points at.
        await using var older = await fixture.Storage.OpenReadAsync(first);
        Assert.Equal("one", await ReadAllAsync(older!.Content));
    }

    [Fact]
    public async Task OpenReadAsync_returns_null_for_an_unknown_path()
    {
        var file = await fixture.Storage.OpenReadAsync("2026/01/does-not-exist.pdf");
        Assert.Null(file);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_blob_and_reports_whether_it_existed()
    {
        var path = await fixture.Storage.SaveAsync(
            AzuriteFixture.StreamOf("temp"), "temp.txt", "text/plain");

        Assert.True(await fixture.Storage.ExistsAsync(path));
        Assert.True(await fixture.Storage.DeleteAsync(path));

        Assert.False(await fixture.Storage.ExistsAsync(path));
        // Second delete is a no-op rather than an error.
        Assert.False(await fixture.Storage.DeleteAsync(path));
    }

    [Fact]
    public async Task DeleteManyAsync_tolerates_paths_that_are_already_gone()
    {
        var kept = await fixture.Storage.SaveAsync(
            AzuriteFixture.StreamOf("keep"), "keep.txt", "text/plain");
        var doomed = await fixture.Storage.SaveAsync(
            AzuriteFixture.StreamOf("bye"), "bye.txt", "text/plain");

        // Mirrors a real cleanup: some paths are live, some already deleted,
        // some never existed. None of that may throw.
        await fixture.Storage.DeleteManyAsync([doomed, "2026/01/missing.txt", doomed]);

        Assert.False(await fixture.Storage.ExistsAsync(doomed));
        Assert.True(await fixture.Storage.ExistsAsync(kept));
    }

    [Fact]
    public async Task SaveAsync_round_trips_binary_content_unchanged()
    {
        var bytes = new byte[4096];
        Random.Shared.NextBytes(bytes);

        var path = await fixture.Storage.SaveAsync(
            new MemoryStream(bytes), "image.png", "image/png");

        await using var file = await fixture.Storage.OpenReadAsync(path);
        using var buffer = new MemoryStream();
        await file!.Content.CopyToAsync(buffer);

        Assert.Equal(bytes, buffer.ToArray());
    }
}
