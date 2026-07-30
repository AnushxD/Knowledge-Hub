namespace DocHub.Integrations.Embeddings;

/// <summary>
/// Strongly-typed configuration for the embedding provider, bound from the
/// "Embeddings" section.
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>Local Ollama — free, no API key, and the default.</summary>
    public const string OllamaProvider = "ollama";

    /// <summary>
    /// Deterministic in-process vectors. No network and no model download,
    /// which is what makes tests fast and hermetic. Its similarity is purely
    /// lexical, so it is not a substitute for a real model in development.
    /// </summary>
    public const string HashingProvider = "hashing";

    public string Provider { get; set; } = OllamaProvider;

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>Must equal the width the chunk table was migrated with.</summary>
    public int Dimensions { get; set; } = 768;

    /// <summary>Passages sent per request. Larger batches trade memory for fewer round trips.</summary>
    public int BatchSize { get; set; } = 16;

    /// <summary>
    /// Generous by default: the first call after a container start pays for
    /// loading the model into memory, and a timeout there would fail an
    /// ingestion job for no real reason.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// How long Ollama keeps the embedding model resident. See
    /// <c>LlmOptions.KeepAlive</c> — the same reasoning, and this model is small
    /// enough that keeping it loaded costs little.
    ///
    /// It matters on every question, not just ingestion: the query is embedded
    /// before retrieval can run, so an evicted model delays the answer before
    /// the assistant has even been asked.
    /// </summary>
    public string KeepAlive { get; set; } = "30m";

    /// <summary>
    /// Task prefix for stored passages. Retrieval models such as
    /// nomic-embed-text are trained with these and lose accuracy without them;
    /// set both to empty for a model that does not use them.
    /// </summary>
    public string DocumentPrefix { get; set; } = "search_document: ";

    /// <summary>Task prefix for queries. See <see cref="DocumentPrefix"/>.</summary>
    public string QueryPrefix { get; set; } = "search_query: ";
}
