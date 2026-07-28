using System.Text;
using DocHub.Services.Ingestion.Extraction;

namespace DocHub.Services.Tests;

public sealed class TextExtractorTests
{
    private static Stream StreamOf(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task Markdown_headings_become_section_references()
    {
        const string markdown = """
            # Remote Access

            Intro paragraph.

            ## Connecting

            Use the VPN client.

            ## Printing

            Printing is unavailable.
            """;

        var extracted = await new PlainTextExtractor().ExtractAsync(StreamOf(markdown), "md");

        // These are what a citation shows, so they have to survive extraction.
        Assert.Equal(
            ["Remote Access", "Connecting", "Printing"],
            extracted.Sections.Select(section => section.SectionRef));

        // The heading stays in the body too: it gives the embedding the topic
        // words the passage would otherwise lack.
        Assert.StartsWith("## Connecting", extracted.Sections[1].Text);
    }

    [Fact]
    public async Task Plain_text_is_one_section_with_no_invented_structure()
    {
        var extracted = await new PlainTextExtractor()
            .ExtractAsync(StreamOf("line one\nline two"), "txt");

        var section = Assert.Single(extracted.Sections);
        Assert.Null(section.SectionRef);
    }

    [Fact]
    public async Task A_byte_order_mark_does_not_leak_into_the_text()
    {
        // Windows editors leave these behind; unhandled they appear as stray
        // characters at the top of the first chunk.
        var withBom = new MemoryStream([
            0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Hello world"),
        ]);

        var extracted = await new PlainTextExtractor().ExtractAsync(withBom, "txt");

        Assert.Equal("Hello world", extracted.Sections[0].Text);
    }

    [Fact]
    public async Task An_empty_file_reports_itself_as_empty()
    {
        var extracted = await new PlainTextExtractor().ExtractAsync(StreamOf("   \n  "), "txt");

        // The pipeline turns this into a visible failure rather than indexing
        // a document with nothing in it.
        Assert.True(extracted.IsEmpty);
    }

    [Fact]
    public async Task A_file_that_is_not_really_a_pdf_fails_with_an_actionable_message()
    {
        var exception = await Assert.ThrowsAsync<TextExtractionException>(
            () => new PdfTextExtractor().ExtractAsync(StreamOf("not a pdf"), "pdf"));

        Assert.Contains("could not be read", exception.Message);
    }

    [Fact]
    public void Every_supported_extension_resolves_to_exactly_one_extractor()
    {
        var registry = new TextExtractorRegistry(
            [new PlainTextExtractor(), new PdfTextExtractor(), new OpenXmlTextExtractor()]);

        Assert.NotNull(registry.Find("md"));
        Assert.NotNull(registry.Find("pdf"));
        Assert.NotNull(registry.Find("docx"));
        Assert.NotNull(registry.Find("xlsx"));

        // A leading dot is accepted, since callers hold extensions both ways.
        Assert.NotNull(registry.Find(".pptx"));

        // Unsupported formats resolve to nothing rather than to a guess.
        Assert.Null(registry.Find("png"));
        Assert.Null(registry.Find("doc"));
    }

    [Fact]
    public void Two_extractors_claiming_one_extension_is_a_startup_error()
    {
        // Silently picking one would make the parser that runs depend on DI
        // registration order.
        Assert.Throws<InvalidOperationException>(
            () => new TextExtractorRegistry([new PlainTextExtractor(), new PlainTextExtractor()]));
    }
}
