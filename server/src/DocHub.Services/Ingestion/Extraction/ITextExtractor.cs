namespace DocHub.Services.Ingestion.Extraction;

/// <summary>
/// A contiguous run of text plus where in the document it came from.
///
/// Extractors emit the finest natural unit a format offers — a PDF page, a
/// slide, a Markdown heading's body — and the chunker regroups them. Keeping
/// that structure instead of returning one flat string is what lets a citation
/// say "Page 4" later.
/// </summary>
/// <param name="SectionRef">
/// Human-readable location, or null when the format offers nothing meaningful.
/// </param>
public sealed record ExtractedSection(string Text, string? SectionRef);

/// <summary>The text of a whole document, in reading order.</summary>
public sealed record ExtractedText(IReadOnlyList<ExtractedSection> Sections)
{
    public static ExtractedText Empty { get; } = new([]);

    /// <summary>
    /// True when nothing usable came out — a scanned PDF with no text layer, an
    /// empty file. The pipeline treats this as a failure the user should see
    /// rather than indexing a document with nothing in it.
    /// </summary>
    public bool IsEmpty =>
        Sections.All(section => string.IsNullOrWhiteSpace(section.Text));
}

/// <summary>
/// Pulls plain text out of one family of file formats.
///
/// Implementations are pure transformations over a stream — no network, no
/// database — which is why they live in the Service layer rather than in
/// Integrations, and why they can be tested with a byte array and no fixture.
/// </summary>
public interface ITextExtractor
{
    /// <summary>Extensions handled, lower-case and without a leading dot.</summary>
    IReadOnlySet<string> Extensions { get; }

    /// <summary>
    /// Reads <paramref name="content"/> to the end and returns its text.
    /// Throws <see cref="TextExtractionException"/> when the file is malformed.
    /// </summary>
    /// <param name="extension">
    /// Which of <see cref="Extensions"/> this file is. One extractor covers a
    /// family of related formats — Word, PowerPoint and Excel share a container
    /// but not a structure — and this is how it tells them apart.
    /// </param>
    Task<ExtractedText> ExtractAsync(
        Stream content,
        string extension,
        CancellationToken ct = default);
}

/// <summary>Finds the extractor for a file, if the format is supported at all.</summary>
public interface ITextExtractorRegistry
{
    /// <summary>Every supported extension, sorted — surfaced in the UI and in errors.</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>The extractor for an extension, or null when the format is unsupported.</summary>
    ITextExtractor? Find(string extension);
}

/// <summary>
/// A file could not be read as text.
///
/// Permanent by nature — a corrupt PDF will still be corrupt on the next
/// attempt — so the pipeline records it against the document instead of
/// retrying, unlike a transient embedding failure.
/// </summary>
public sealed class TextExtractionException(string message, Exception? inner = null)
    : Exception(message, inner);
