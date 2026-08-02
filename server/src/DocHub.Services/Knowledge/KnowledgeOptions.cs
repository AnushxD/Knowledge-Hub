namespace DocHub.Services.Knowledge;

/// <summary>
/// How the assistant treats its knowledge sources, bound from the "Knowledge"
/// section.
/// </summary>
public sealed class KnowledgeOptions
{
    public const string SectionName = "Knowledge";

    /// <summary>
    /// How long any one source may take before it is left out of the answer.
    ///
    /// This is what stops a hung source becoming a hung application. Failure
    /// isolation already covers a source that *throws*; a source that simply
    /// never replies is the harder case, because the fan-out waits for all of
    /// them — so one unreachable MCP server would stall every question,
    /// including the ones the documents alone could have answered.
    ///
    /// Ten seconds is deliberately generous. It is not a latency target; it is
    /// the point past which waiting is worse than answering without that
    /// source, and a healthy source should never come close.
    /// </summary>
    public int SourceTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How far a chunk may sit from the question, in cosine distance, and still
    /// be offered to the assistant as grounding. Nothing beyond it is retrieved.
    ///
    /// A vector index always has a nearest neighbour, so without a floor every
    /// question retrieves something and the model is handed passages about
    /// whatever happened to be least unlike it. Measured on this corpus with
    /// nomic-embed-text: a question the documents answer lands around 0.31, and
    /// a question they do not lands at 0.54 and up. 0.5 sits in that gap.
    ///
    /// Corpus- and model-dependent, so it is configuration rather than a
    /// constant: too low silently refuses answerable questions, too high lets
    /// the noise back in. It applies to retrieval for the assistant only — the
    /// search screen shows what it finds and lets a person judge it.
    /// </summary>
    public double MaxPassageDistance { get; set; } = 0.5;
}
