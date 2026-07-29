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
}
