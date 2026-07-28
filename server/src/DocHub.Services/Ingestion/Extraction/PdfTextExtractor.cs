using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace DocHub.Services.Ingestion.Extraction;

/// <summary>
/// Extracts a PDF's text layer with PdfPig, one section per page so citations
/// can name the page.
///
/// Scanned PDFs have no text layer and come out empty; the pipeline reports
/// that as a failure the user can act on. OCR is deliberately not attempted
/// here — it is a different class of dependency (and cost) and belongs in a
/// later phase, not hidden inside a text extractor.
/// </summary>
internal sealed class PdfTextExtractor : ITextExtractor
{
    public IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pdf" };

    public Task<ExtractedText> ExtractAsync(
        Stream content,
        string extension,
        CancellationToken ct = default)
    {
        // PdfPig is synchronous and needs random access, so the file is buffered
        // first. Uploads are capped at 25 MB, which bounds this.
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        buffer.Position = 0;

        try
        {
            using var pdf = PdfDocument.Open(buffer);
            var sections = new List<ExtractedSection>();

            foreach (var page in pdf.GetPages())
            {
                ct.ThrowIfCancellationRequested();

                // NearestNeighbourWordExtractor rather than page.Text: the raw
                // property concatenates glyphs in content-stream order, which
                // loses the spaces between words in most real PDFs.
                var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
                var text = string.Join(' ', words.Select(word => word.Text)).Trim();

                if (text.Length > 0)
                    sections.Add(new ExtractedSection(text, $"Page {page.Number}"));
            }

            return Task.FromResult(new ExtractedText(sections));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new TextExtractionException(
                "The PDF could not be read. It may be corrupt or password-protected.",
                exception);
        }
    }
}
