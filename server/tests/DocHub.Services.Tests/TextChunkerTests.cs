using DocHub.Services.Ingestion;
using DocHub.Services.Ingestion.Extraction;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Tests;

/// <summary>
/// Chunking is where retrieval quality is won or lost, and it needs no
/// infrastructure to test — so these run as plain unit tests rather than
/// against the stack fixture.
/// </summary>
public sealed class TextChunkerTests
{
    private static TextChunker ChunkerWith(Action<IngestionOptions>? configure = null)
    {
        var options = new IngestionOptions();
        configure?.Invoke(options);
        return new TextChunker(Options.Create(options));
    }

    private static ExtractedText TextOf(params (string Body, string? Section)[] sections) =>
        new([.. sections.Select(s => new ExtractedSection(s.Body, s.Section))]);

    /// <summary>Paragraphs of roughly <paramref name="tokens"/> tokens each.</summary>
    private static string Paragraphs(int count, int tokens) =>
        string.Join(
            "\n\n",
            Enumerable.Range(0, count).Select(i => $"P{i} " + new string('x', tokens * 4)));

    [Fact]
    public void A_short_document_stays_one_chunk()
    {
        var chunks = ChunkerWith().Chunk(TextOf((Paragraphs(2, 60), "Intro")));

        Assert.Single(chunks);
        Assert.Equal("Intro", chunks[0].SectionRef);
    }

    [Fact]
    public void Long_text_is_split_at_roughly_the_target_size()
    {
        var chunks = ChunkerWith(o => o.TargetTokens = 200)
            .Chunk(TextOf((Paragraphs(12, 60), null)));

        Assert.True(chunks.Count > 1, "long text should produce several chunks");

        // Overlap means a chunk can exceed the target somewhat, but never wildly.
        Assert.All(chunks, chunk => Assert.True(
            chunk.TokenCount <= 400,
            $"chunk of {chunk.TokenCount} tokens is far past the 200 target"));
    }

    [Fact]
    public void Consecutive_chunks_overlap_so_nothing_is_lost_at_a_boundary()
    {
        var chunks = ChunkerWith(o =>
        {
            o.TargetTokens = 120;
            o.OverlapTokens = 60;
        }).Chunk(TextOf((Paragraphs(10, 50), null)));

        Assert.True(chunks.Count > 1);

        // The tail of one chunk should reappear at the head of the next,
        // otherwise a passage cut down the middle is unfindable.
        var overlaps = chunks
            .Zip(chunks.Skip(1), (first, second) =>
                first.Text.Split("\n\n").Any(block => second.Text.Contains(block)))
            .ToList();

        Assert.Contains(true, overlaps);
    }

    [Fact]
    public void A_chunk_never_spans_two_sections()
    {
        // Both sections easily fit in one chunk, so they would be merged if the
        // chunker allowed a chunk to span sections.
        var chunks = ChunkerWith().Chunk(TextOf(
            ("Alpha content covering the first topic in enough detail to clear the minimum "
             + "chunk size and stand on its own as a retrievable passage.", "Page 1"),
            ("Beta content covering the second topic, likewise long enough to be kept rather "
             + "than folded away as an undersized fragment.", "Page 2")));

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Page 1", chunks[0].SectionRef);
        Assert.Equal("Page 2", chunks[1].SectionRef);

        // A citation pointing at "Page 1" must not contain Page 2's text.
        Assert.DoesNotContain("Beta", chunks[0].Text);
    }

    [Fact]
    public void A_heading_with_no_body_is_not_indexed_on_its_own()
    {
        var chunks = ChunkerWith().Chunk(TextOf(
            ("# Remote Access Setup", "Remote Access Setup"),
            ("A real paragraph with enough substance to be worth retrieving later on.", "Details")));

        // The bare heading would otherwise embed to the document's topic and
        // outrank the passage that actually answers a question about it.
        Assert.Single(chunks);
        Assert.Equal("Details", chunks[0].SectionRef);
    }

    [Fact]
    public void A_document_that_is_only_a_short_line_still_gets_indexed()
    {
        var chunks = ChunkerWith().Chunk(TextOf(("Ping the service desk.", null)));

        // Dropping every undersized chunk must never leave a document with
        // nothing at all — it would silently vanish from search.
        Assert.Single(chunks);
        Assert.Contains("service desk", chunks[0].Text);
    }

    [Fact]
    public void Ordinals_are_contiguous_from_zero()
    {
        var chunks = ChunkerWith(o => o.TargetTokens = 150)
            .Chunk(TextOf((Paragraphs(10, 60), "A"), ("# Bare heading", "B")));

        // Ordinals are a chunk's identity in a citation URL and carry a unique
        // index, so a gap left by filtering is a real bug.
        Assert.Equal(
            Enumerable.Range(0, chunks.Count),
            chunks.Select(chunk => chunk.Ordinal));
    }

    [Fact]
    public void Text_with_no_paragraph_or_sentence_breaks_is_still_split()
    {
        // Minified JSON, a base64 blob — nothing to split on but length.
        var blob = new string('a', 8000);

        var chunks = ChunkerWith(o => o.TargetTokens = 200).Chunk(TextOf((blob, null)));

        Assert.True(chunks.Count > 1, "an unbroken blob must not become one huge chunk");
    }

    [Fact]
    public void Chunk_count_is_capped_so_one_upload_cannot_run_away()
    {
        var chunks = ChunkerWith(o =>
        {
            o.TargetTokens = 30;
            o.OverlapTokens = 5;
            o.MaxChunksPerDocument = 5;
        }).Chunk(TextOf((Paragraphs(200, 30), null)));

        Assert.Equal(5, chunks.Count);
    }
}
