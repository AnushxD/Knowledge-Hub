namespace DocHub.Integrations.Embeddings;

/// <summary>
/// Turns text into vectors for the pgvector half of hybrid search.
///
/// Documents and queries are embedded through separate methods rather than one
/// shared call: retrieval models are trained asymmetrically and want a
/// different task prefix for each side, and collapsing them measurably hurts
/// recall. Callers should never have to know which model is behind this.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Identifies the provider in logs, health checks and stored metadata.</summary>
    string Name { get; }

    /// <summary>
    /// Width of the vectors produced. Must match the embedding column the
    /// schema was migrated with — the API checks this at startup, because a
    /// mismatch discovered at write time would already have wasted a full
    /// ingestion run.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds passages for storage, in the order given. Batched because every
    /// provider charges — in latency or in money — per call, not per token.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default);

    /// <summary>Embeds a user's search query.</summary>
    Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Whether the provider can serve requests right now, with a reason when it
    /// cannot. Used by the readiness check so a missing model is reported as a
    /// setup problem rather than as a failed upload later.
    /// </summary>
    Task<EmbeddingAvailability> CheckAvailabilityAsync(CancellationToken ct = default);
}

/// <summary>Result of a provider readiness probe.</summary>
/// <param name="IsAvailable">False when embedding calls would currently fail.</param>
/// <param name="Detail">Human-readable status, naming the fix when there is one.</param>
public sealed record EmbeddingAvailability(bool IsAvailable, string Detail);
