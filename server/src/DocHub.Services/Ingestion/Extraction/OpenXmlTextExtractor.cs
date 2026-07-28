using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Presentation;
using DrawingText = DocumentFormat.OpenXml.Drawing.Text;

namespace DocHub.Services.Ingestion.Extraction;

/// <summary>
/// Extracts Word, PowerPoint and Excel content with the OpenXML SDK.
///
/// The three share a container format but nothing else, so each gets its own
/// pass — and each produces the section reference that format's readers
/// actually use: a heading in Word, a slide number in PowerPoint, a sheet name
/// in Excel. Legacy binary .doc/.ppt/.xls are not OpenXML and are not
/// supported.
/// </summary>
internal sealed class OpenXmlTextExtractor : ITextExtractor
{
    public IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "docx", "docm", "pptx", "pptm", "xlsx", "xlsm",
        };

    public Task<ExtractedText> ExtractAsync(
        Stream content,
        string extension,
        CancellationToken ct = default)
    {
        // The SDK needs a seekable stream it can read repeatedly.
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        buffer.Position = 0;

        try
        {
            var sections = extension.ToLowerInvariant() switch
            {
                "docx" or "docm" => ExtractWord(buffer, ct),
                "pptx" or "pptm" => ExtractPresentation(buffer, ct),
                "xlsx" or "xlsm" => ExtractSpreadsheet(buffer, ct),
                _ => throw new TextExtractionException($".{extension} is not an OpenXML format."),
            };

            return Task.FromResult(new ExtractedText(sections));
        }
        catch (TextExtractionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new TextExtractionException(
                $"The .{extension} file could not be read. It may be corrupt or password-protected.",
                exception);
        }
    }

    /// <summary>
    /// Walks paragraphs in order, starting a new section at every heading so a
    /// citation can name it. Text outside the main body — headers, footers,
    /// footnotes — is skipped: it repeats on every page and would otherwise
    /// dominate the index with boilerplate.
    /// </summary>
    private static List<ExtractedSection> ExtractWord(Stream stream, CancellationToken ct)
    {
        using var document = WordprocessingDocument.Open(stream, isEditable: false);

        var body = document.MainDocumentPart?.Document.Body;
        if (body is null) return [];

        var sections = new List<ExtractedSection>();
        var buffer = new StringBuilder();
        string? heading = null;

        void Flush()
        {
            var text = buffer.ToString().Trim();
            if (text.Length > 0)
                sections.Add(new ExtractedSection(text, heading));

            buffer.Clear();
        }

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            ct.ThrowIfCancellationRequested();

            var text = paragraph.InnerText.Trim();
            if (text.Length == 0) continue;

            var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

            if (styleId is not null &&
                styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                heading = text;
            }

            buffer.AppendLine(text);
        }

        Flush();

        return sections;
    }

    /// <summary>
    /// One section per slide. Slide order comes from the presentation's slide
    /// id list rather than from part order, which is arbitrary in the package.
    /// </summary>
    private static List<ExtractedSection> ExtractPresentation(Stream stream, CancellationToken ct)
    {
        using var document = PresentationDocument.Open(stream, isEditable: false);

        var presentationPart = document.PresentationPart;
        if (presentationPart?.Presentation.SlideIdList is null) return [];

        var sections = new List<ExtractedSection>();
        var slideNumber = 0;

        foreach (var slideId in presentationPart.Presentation.SlideIdList.Elements<SlideId>())
        {
            ct.ThrowIfCancellationRequested();
            slideNumber++;

            if (slideId.RelationshipId?.Value is not { } relationshipId) continue;
            if (presentationPart.GetPartById(relationshipId) is not SlidePart slidePart) continue;

            // Drawing text runs, so each text box contributes a separate line
            // instead of every shape running together.
            var lines = slidePart.Slide
                .Descendants<DrawingText>()
                .Select(run => run.Text.Trim())
                .Where(text => text.Length > 0);

            var text = string.Join(Environment.NewLine, lines).Trim();

            if (text.Length > 0)
                sections.Add(new ExtractedSection(text, $"Slide {slideNumber}"));
        }

        return sections;
    }

    /// <summary>
    /// One section per worksheet, rows rendered as tab-separated lines.
    ///
    /// A spreadsheet is not prose, and this makes no attempt to pretend
    /// otherwise — the goal is that a cell's contents are findable and can be
    /// cited to the right sheet, not that the layout survives.
    /// </summary>
    private static List<ExtractedSection> ExtractSpreadsheet(Stream stream, CancellationToken ct)
    {
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);

        var workbookPart = document.WorkbookPart;
        if (workbookPart?.Workbook.Sheets is null) return [];

        // Shared strings are stored once and referenced by index; without this
        // lookup every text cell reads as a number.
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
            .Elements<SharedStringItem>()
            .Select(item => item.InnerText)
            .ToArray() ?? [];

        var sections = new List<ExtractedSection>();

        foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
        {
            ct.ThrowIfCancellationRequested();

            if (sheet.Id?.Value is not { } relationshipId) continue;
            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart) continue;

            var builder = new StringBuilder();

            foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
            {
                var values = row.Elements<Cell>()
                    .Select(cell => CellText(cell, sharedStrings))
                    .Where(value => value.Length > 0);

                var line = string.Join('\t', values);
                if (line.Length > 0)
                    builder.AppendLine(line);
            }

            var text = builder.ToString().Trim();
            if (text.Length > 0)
                sections.Add(new ExtractedSection(text, sheet.Name?.Value));
        }

        return sections;
    }

    private static string CellText(Cell cell, string[] sharedStrings)
    {
        var raw = cell.CellValue?.InnerText ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(raw, out var index) &&
            index >= 0 && index < sharedStrings.Length)
        {
            return sharedStrings[index].Trim();
        }

        return raw.Trim();
    }
}
