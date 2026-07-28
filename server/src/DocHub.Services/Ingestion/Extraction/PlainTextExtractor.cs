using System.Text;

namespace DocHub.Services.Ingestion.Extraction;

/// <summary>
/// Handles formats that are already text: Markdown, plain text, config files,
/// SQL and the like.
///
/// Markdown gets one extra pass — the file is split on ATX headings so each
/// section carries its heading as a citation reference. Everything else is
/// returned as a single section, because inventing structure that is not in the
/// file would make citations claim more precision than they have.
/// </summary>
internal sealed class PlainTextExtractor : ITextExtractor
{
    /// <summary>Extensions treated as Markdown, so headings become section refs.</summary>
    private static readonly HashSet<string> MarkdownExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "md", "markdown", "mdx" };

    public IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "txt", "md", "markdown", "mdx", "csv", "tsv", "json", "yaml", "yml",
            "xml", "sql", "log", "ini", "toml", "cfg", "conf", "env", "rst", "adoc",
        };

    public async Task<ExtractedText> ExtractAsync(
        Stream content,
        string extension,
        CancellationToken ct = default)
    {
        // detectEncodingFromByteOrderMarks handles the UTF-8/UTF-16 BOMs that
        // Windows editors leave behind; without it they surface as stray
        // characters at the top of the first chunk.
        using var reader = new StreamReader(
            content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var text = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(text))
            return ExtractedText.Empty;

        return MarkdownExtensions.Contains(extension)
            ? new ExtractedText(SplitMarkdown(text))
            : new ExtractedText([new ExtractedSection(text, null)]);
    }

    /// <summary>
    /// Splits on ATX headings ("## Setup"), attributing each block to the
    /// nearest heading above it. Setext headings and fenced code blocks that
    /// contain '#' are not special-cased — the cost of a mislabelled section
    /// ref is small, and the alternative is a full Markdown parser for a
    /// citation label.
    /// </summary>
    private static List<ExtractedSection> SplitMarkdown(string text)
    {
        var sections = new List<ExtractedSection>();
        var buffer = new StringBuilder();
        string? heading = null;

        void Flush()
        {
            if (buffer.Length == 0) return;

            var body = buffer.ToString().Trim();
            if (body.Length > 0)
                sections.Add(new ExtractedSection(body, heading));

            buffer.Clear();
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('#') && trimmed.TrimStart('#').StartsWith(' '))
            {
                Flush();
                heading = trimmed.TrimStart('#').Trim();
                // The heading stays in the body too: a chunk that opens with
                // its own title reads better as a search result, and gives the
                // embedding the topic words it would otherwise lack.
                buffer.AppendLine(trimmed);
                continue;
            }

            buffer.AppendLine(line);
        }

        Flush();

        return sections.Count > 0 ? sections : [new ExtractedSection(text.Trim(), null)];
    }
}
