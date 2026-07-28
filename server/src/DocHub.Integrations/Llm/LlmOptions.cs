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
    /// Generous by default: the first call after a container start pays for
    /// loading the model into memory, and a timeout there would fail a
    /// perfectly good question.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 180;
}
