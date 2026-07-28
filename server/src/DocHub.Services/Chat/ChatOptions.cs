namespace DocHub.Services.Chat;

/// <summary>
/// Tuning for the assistant, bound from the "Chat" section.
/// </summary>
public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>
    /// Passages retrieved and offered to the model per question.
    ///
    /// The main quality lever, and it cuts both ways: too few and the answer is
    /// missing context that exists; too many and the relevant passage is buried
    /// among near-misses the model then blends together.
    /// </summary>
    public int PassageCount { get; set; } = 6;

    /// <summary>
    /// Prior question-and-answer pairs replayed for follow-ups. Enough for
    /// "what about the second one?" to resolve, without letting the transcript
    /// crowd the retrieved passages out of the model's context.
    /// </summary>
    public int HistoryTurns { get; set; } = 4;

    /// <summary>
    /// Rejects pasted documents. The assistant answers from the index, so a
    /// wall of text in the question is a misunderstanding of what it does —
    /// and it would push the real sources out of context.
    /// </summary>
    public int MaxQuestionLength { get; set; } = 2000;
}
