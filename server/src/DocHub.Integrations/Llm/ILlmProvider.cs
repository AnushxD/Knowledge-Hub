namespace DocHub.Integrations.Llm;

/// <summary>Who produced a turn in the conversation sent to the model.</summary>
public enum LlmRole
{
    User,
    Assistant,
}

/// <summary>One turn of conversation history.</summary>
public sealed record LlmMessage(LlmRole Role, string Content);

/// <summary>Result of a provider readiness probe.</summary>
/// <param name="Detail">Human-readable status, naming the fix when there is one.</param>
public sealed record LlmAvailability(bool IsAvailable, string Detail);

/// <summary>
/// Generates an answer from a grounded prompt.
///
/// Streaming-only by design: a grounded answer over several retrieved passages
/// takes seconds to produce, and a user watching a blank screen for that long
/// assumes the thing is broken. Anything that needs the whole answer can
/// accumulate the stream.
///
/// The Service layer only ever sees this interface, so which model runs — a
/// local one or a hosted API — never reaches the RAG orchestrator.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Identifies the provider in logs, health checks and the UI.</summary>
    string Name { get; }

    /// <summary>
    /// Streams the answer in fragments as the model produces them.
    ///
    /// <paramref name="systemPrompt"/> carries the grounding rules and the
    /// retrieved context; <paramref name="messages"/> is the conversation so
    /// far, oldest first, ending with the question being asked.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        IReadOnlyList<LlmMessage> messages,
        CancellationToken ct = default);

    /// <summary>
    /// Whether the provider can serve requests right now. Used by the readiness
    /// check so a missing model reads as a setup problem rather than as a
    /// failed question later.
    /// </summary>
    Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken ct = default);
}

/// <summary>
/// A generation call failed.
///
/// Distinct from a generic exception so the chat orchestrator can tell "the
/// model is unavailable" apart from "this question cannot be answered from the
/// retrieved context" — the second is a normal outcome, not an error.
/// </summary>
public sealed class LlmException(string message, Exception? inner = null)
    : Exception(message, inner);
