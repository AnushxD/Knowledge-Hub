using Microsoft.Extensions.Options;

namespace DocHub.Integrations.Embeddings;

/// <summary>
/// Deterministic embeddings computed in-process by the hashing trick: each word
/// is hashed to a dimension and accumulated, then the vector is normalised.
///
/// This exists so the ingestion pipeline and every test that exercises it can
/// run with no model, no network and no download. Similarity here is lexical
/// overlap only — it will match "invoice" to "invoice" and has no idea that
/// "invoice" relates to "billing". That is fine for asserting that the pipeline
/// wires up correctly, and is the reason this is not the default provider.
/// </summary>
internal sealed class HashingEmbeddingProvider(IOptions<EmbeddingOptions> options)
    : IEmbeddingProvider
{
    private readonly int dimensions = options.Value.Dimensions;

    public string Name => $"hashing({dimensions})";

    public int Dimensions => dimensions;

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(Embed)]);

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(Embed(text));

    public Task<EmbeddingAvailability> CheckAvailabilityAsync(CancellationToken ct = default) =>
        Task.FromResult(new EmbeddingAvailability(
            true, "In-process hashing embeddings; no external model in use."));

    private float[] Embed(string text)
    {
        var vector = new float[dimensions];

        foreach (var token in Tokenize(text))
        {
            var hash = (int)(Hash(token) % (uint)dimensions);
            // A second hash bit decides the sign, so unrelated words that land
            // on the same dimension cancel out rather than always reinforcing.
            var sign = (Hash(token + "#") & 1) == 0 ? 1f : -1f;
            vector[hash] += sign;
        }

        return EmbeddingVector.Normalize(vector);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (var token in text.Split(
            [' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}',
             '"', '\'', '/', '\\', '-', '_', '*', '#', '`', '|', '<', '>', '='],
            StringSplitOptions.RemoveEmptyEntries))
        {
            yield return token.ToLowerInvariant();
        }
    }

    /// <summary>
    /// FNV-1a. Chosen over string.GetHashCode because that is randomised per
    /// process — vectors written by one run have to stay comparable with the
    /// next, or search silently degrades after a restart.
    /// </summary>
    private static uint Hash(string value)
    {
        const uint OffsetBasis = 2166136261;
        const uint Prime = 16777619;

        var hash = OffsetBasis;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= Prime;
        }

        return hash;
    }
}
