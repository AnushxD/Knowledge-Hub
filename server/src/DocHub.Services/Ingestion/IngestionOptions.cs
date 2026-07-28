namespace DocHub.Services.Ingestion;

/// <summary>
/// Tuning for the ingestion pipeline, bound from the "Ingestion" section.
///
/// The chunk-size defaults are the main lever on retrieval quality: too small
/// and a chunk loses the context that makes it answerable, too large and the
/// embedding blurs several topics together and matches none of them well.
/// </summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Target chunk size. Around 800 tokens is roughly three paragraphs — big
    /// enough to answer a question on its own, small enough that its embedding
    /// still points at one topic.
    /// </summary>
    public int TargetTokens { get; set; } = 800;

    /// <summary>
    /// Tokens repeated from the end of the previous chunk, so an answer that
    /// straddles a boundary is not lost by whichever side gets cut. Roughly 15%
    /// of the target.
    /// </summary>
    public int OverlapTokens { get; set; } = 120;

    /// <summary>
    /// Chunks below this are folded into their neighbour rather than stored.
    /// A stray heading on its own embeds to noise and crowds out real results.
    /// </summary>
    public int MinTokens { get; set; } = 24;

    /// <summary>
    /// Ceiling on chunks per document, so one pathological upload cannot spend
    /// an unbounded amount of embedding time and table space.
    /// </summary>
    public int MaxChunksPerDocument { get; set; } = 2000;
}
