namespace DocHub.Integrations.Llm;

/// <summary>
/// Strongly-typed configuration for the answer-generating model, bound from the
/// "Llm" section.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Local Ollama — free, no API key, and the default.</summary>
    public const string OllamaProvider = "ollama";

    public string Provider { get; set; } = OllamaProvider;

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.2:3b";

    /// <summary>
    /// Ceiling on the answer length. Generous rather than tight: a grounded
    /// answer that cites several passages runs long, and truncating one
    /// mid-citation is worse than a slightly slow response.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>
    /// Low on purpose. The job is to restate what the retrieved passages say,
    /// not to write creatively — and sampling variety is exactly how a model
    /// starts inventing details the sources do not contain.
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Context window offered to the model, in tokens.
    ///
    /// Must hold the grounding rules, the worked example, every retrieved
    /// passage, and the replayed conversation — with the default six passages
    /// of up to 800 tokens each, that is already past 5,000.
    ///
    /// Sized here rather than left to Ollama, whose default is 2048 and which
    /// enforces it by dropping the overflow without saying so. The visible
    /// symptom of getting this wrong is not an error: it is an assistant that
    /// answers without citations, because the passages it was told to cite were
    /// cut before it saw them.
    ///
    /// Raising it costs memory. Lower it on a constrained machine, but lower
    /// <c>Chat:PassageCount</c> to match rather than letting the prompt
    /// overflow again.
    /// </summary>
    public int ContextTokens { get; set; } = 8192;

    /// <summary>
    /// Generous by default: the first call after a container start pays for
    /// loading the model into memory, and a timeout there would fail a
    /// perfectly good question.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// How long Ollama keeps the model in memory after a request, in its own
    /// duration format — "30m", "1h", or "-1" to keep it loaded indefinitely.
    ///
    /// Ollama's own default is five minutes, which is shorter than the gaps
    /// between questions on an internal tool. Every gap longer than that costs
    /// a full reload before the answer even starts — measured at roughly three
    /// seconds for a 3B model, and worse for a larger one. That delay lands on
    /// whoever asks the first question after a quiet spell, which is exactly
    /// when the app feels slowest.
    ///
    /// The cost is memory: the model stays resident. On a box doing nothing
    /// else, that is what the memory is for.
    /// </summary>
    public string KeepAlive { get; set; } = "30m";
}
