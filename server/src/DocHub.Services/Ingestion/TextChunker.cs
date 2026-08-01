using System.Text;
using DocHub.DataAccess;
using DocHub.Services.Ingestion.Extraction;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Ingestion;

/// <summary>One chunk as produced by the chunker, before it has an embedding.</summary>
public sealed record TextChunk(int Ordinal, string Text, string? SectionRef, int TokenCount);

/// <summary>
/// Regroups extracted sections into overlapping, embedding-sized passages.
/// </summary>
public interface ITextChunker
{
    IReadOnlyList<TextChunk> Chunk(ExtractedText text);
}

/// <summary>
/// Splits on the structure the document already has, and only falls back to
/// blunter cuts when a single block is too big to fit.
///
/// The order — sections, then paragraphs, then sentences, then raw characters —
/// matters: every step down loses more meaning, so each is only reached when
/// the one above cannot produce a small enough piece. Chunks never span two
/// sections, which is what keeps a citation's page or heading unambiguous.
/// </summary>
internal sealed class TextChunker(IOptions<IngestionOptions> options) : ITextChunker
{
    private readonly IngestionOptions options = options.Value;

    public IReadOnlyList<TextChunk> Chunk(ExtractedText text)
    {
        var chunks = new List<TextChunk>();

        foreach (var section in text.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Text)) continue;

            // A section ref is whatever the document called that part of
            // itself, so its length is the author's decision, not ours: a
            // sentence used as a heading, or a line of prose that happens to
            // open with "# ". Clamped once here rather than in each extractor,
            // so no extractor can produce a chunk that will not persist.
            var sectionRef = Truncate.ToFit(
                section.SectionRef, DocHubDbContext.SectionRefMaxLength);

            foreach (var body in ChunkSection(section.Text))
            {
                if (chunks.Count >= options.MaxChunksPerDocument) return Renumber(chunks);

                chunks.Add(new TextChunk(0, body, sectionRef, EstimateTokens(body)));
            }
        }

        // Drop passages too small to answer anything. A bare heading — a
        // section whose whole content is "# Remote Access Setup" — embeds to
        // the document's topic and then outranks the passage that actually
        // answers the question.
        var substantial = chunks
            .Where(chunk => chunk.TokenCount >= options.MinTokens)
            .ToList();

        // Unless that would leave nothing: a genuinely one-line document should
        // still be findable, so keep its largest passage.
        if (substantial.Count == 0 && chunks.Count > 0)
            substantial = [chunks.MaxBy(chunk => chunk.TokenCount)!];

        return Renumber(substantial);
    }

    /// <summary>
    /// Ordinals must be contiguous from zero — they are the chunk's identity in
    /// a citation URL and carry a unique index in the database.
    /// </summary>
    private static IReadOnlyList<TextChunk> Renumber(List<TextChunk> chunks) =>
        [.. chunks.Select((chunk, index) => chunk with { Ordinal = index })];

    private IEnumerable<string> ChunkSection(string text)
    {
        var blocks = SplitIntoBlocks(text);
        var current = new List<string>();
        var currentTokens = 0;

        foreach (var block in blocks)
        {
            var blockTokens = EstimateTokens(block);

            if (currentTokens > 0 && currentTokens + blockTokens > options.TargetTokens)
            {
                yield return Join(current);

                // Carry the tail of the chunk just emitted into the next one, so
                // a passage split down the middle is still fully present in at
                // least one chunk.
                current = TakeOverlap(current);
                currentTokens = current.Sum(EstimateTokens);
            }

            current.Add(block);
            currentTokens += blockTokens;
        }

        // Undersized tails are not filtered here — Chunk() applies the minimum
        // across the whole document, so it can tell "too small to be useful"
        // apart from "all this document has".
        var tail = Join(current);
        if (tail.Length > 0) yield return tail;
    }

    /// <summary>
    /// Breaks a section into the largest pieces that still fit the target,
    /// preferring paragraph boundaries and degrading to sentences and then to
    /// a hard character cut for text that has neither.
    /// </summary>
    private List<string> SplitIntoBlocks(string text)
    {
        var blocks = new List<string>();

        foreach (var paragraph in SplitParagraphs(text))
        {
            if (EstimateTokens(paragraph) <= options.TargetTokens)
            {
                blocks.Add(paragraph);
                continue;
            }

            foreach (var sentence in SplitSentences(paragraph))
            {
                if (EstimateTokens(sentence) <= options.TargetTokens)
                {
                    blocks.Add(sentence);
                    continue;
                }

                // No sentence boundaries at all — minified JSON, a base64 blob,
                // a table rendered as one line. Cut it by length.
                blocks.AddRange(SplitByLength(sentence, options.TargetTokens * CharsPerToken));
            }
        }

        return blocks;
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        foreach (var paragraph in text.Split(
            ["\r\n\r\n", "\n\n", "\r\r"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = paragraph.Trim();
            if (trimmed.Length > 0) yield return trimmed;
        }
    }

    /// <summary>
    /// Sentence split on terminal punctuation followed by whitespace. Crude by
    /// design — "e.g." will split early, and the cost of that is one slightly
    /// short chunk, not a wrong answer.
    /// </summary>
    private static IEnumerable<string> SplitSentences(string paragraph)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < paragraph.Length; i++)
        {
            builder.Append(paragraph[i]);

            var isTerminator = paragraph[i] is '.' or '!' or '?' or '\n';
            var nextIsSpace = i + 1 >= paragraph.Length || char.IsWhiteSpace(paragraph[i + 1]);

            if (isTerminator && nextIsSpace && builder.Length > 0)
            {
                var sentence = builder.ToString().Trim();
                if (sentence.Length > 0) yield return sentence;
                builder.Clear();
            }
        }

        var remainder = builder.ToString().Trim();
        if (remainder.Length > 0) yield return remainder;
    }

    private static IEnumerable<string> SplitByLength(string text, int maxChars)
    {
        for (var start = 0; start < text.Length; start += maxChars)
            yield return text.Substring(start, Math.Min(maxChars, text.Length - start));
    }

    /// <summary>Trailing blocks of a chunk worth about <c>OverlapTokens</c>.</summary>
    private List<string> TakeOverlap(List<string> blocks)
    {
        if (options.OverlapTokens <= 0) return [];

        var overlap = new List<string>();
        var tokens = 0;

        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            var blockTokens = EstimateTokens(blocks[i]);

            // Never carry the whole chunk forward: that would make the next
            // chunk a superset of this one and the loop would not advance.
            if (tokens + blockTokens > options.OverlapTokens && overlap.Count > 0) break;
            if (overlap.Count == blocks.Count - 1) break;

            overlap.Insert(0, blocks[i]);
            tokens += blockTokens;

            if (tokens >= options.OverlapTokens) break;
        }

        return overlap;
    }

    private static string Join(IEnumerable<string> blocks) =>
        string.Join("\n\n", blocks).Trim();

    /// <summary>
    /// Average characters per token for English prose. Used instead of a real
    /// tokenizer: the exact count only has to be good enough to size chunks and
    /// budget prompts, and a tokenizer would tie chunking to one model's
    /// vocabulary.
    /// </summary>
    private const int CharsPerToken = 4;

    public static int EstimateTokens(string text) =>
        Math.Max(1, (int)Math.Ceiling(text.Length / (double)CharsPerToken));
}
