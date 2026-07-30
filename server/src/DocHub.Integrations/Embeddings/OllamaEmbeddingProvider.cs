using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations.Embeddings;

/// <summary>
/// Embeddings from a local Ollama instance.
///
/// Chosen as the default because it costs nothing, needs no API key and keeps
/// document text on the machine — but it is reached over HTTP through the same
/// interface a hosted provider would use, so moving to Voyage or OpenAI later
/// is a registration change plus a re-index, not a change to ingestion or
/// search.
/// </summary>
internal sealed class OllamaEmbeddingProvider(
    HttpClient http,
    IOptions<EmbeddingOptions> options,
    ILogger<OllamaEmbeddingProvider> logger) : IEmbeddingProvider
{
    private readonly EmbeddingOptions options = options.Value;

    public string Name => $"ollama/{this.options.Model}";

    public int Dimensions => this.options.Dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        var results = new List<float[]>(texts.Count);

        // Chunked rather than sent as one request: a whole document can be
        // hundreds of passages, and a single oversized body is the easiest way
        // to hit a timeout that then fails the entire ingestion job.
        foreach (var batch in texts.Chunk(Math.Max(1, options.BatchSize)))
        {
            var prefixed = batch.Select(text => options.DocumentPrefix + text).ToArray();
            results.AddRange(await EmbedAsync(prefixed, ct));
        }

        return results;
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default)
    {
        var embeddings = await EmbedAsync([options.QueryPrefix + text], ct);
        return embeddings[0];
    }

    public async Task<EmbeddingAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            var tags = await http.GetFromJsonAsync<OllamaTagsResponse>("/api/tags", ct);

            var installed = tags?.Models?
                .Select(model => model.Name ?? string.Empty)
                .ToList() ?? [];

            // Ollama reports "nomic-embed-text:latest" for a model pulled as
            // "nomic-embed-text", so compare on the name without the tag.
            var hasModel = installed.Any(name =>
                name.Split(':')[0].Equals(options.Model.Split(':')[0], StringComparison.OrdinalIgnoreCase));

            if (!hasModel)
            {
                return new EmbeddingAvailability(false,
                    $"Ollama is reachable at {options.BaseUrl} but the '{options.Model}' model is "
                    + $"not installed. Run: docker compose exec ollama ollama pull {options.Model}");
            }

            return new EmbeddingAvailability(true, $"Ollama serving '{options.Model}'.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new EmbeddingAvailability(false,
                $"Ollama is not reachable at {options.BaseUrl}. Run: docker compose up -d");
        }
    }

    private async Task<IReadOnlyList<float[]>> EmbedAsync(
        string[] inputs,
        CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(
            "/api/embed", new OllamaEmbedRequest(options.Model, inputs, options.KeepAlive), ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Ollama embedding call failed with {StatusCode}: {Body}",
                (int)response.StatusCode, body);

            throw new EmbeddingException(
                $"Embedding provider '{Name}' returned {(int)response.StatusCode}. {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct);

        if (payload?.Embeddings is null || payload.Embeddings.Count != inputs.Length)
        {
            throw new EmbeddingException(
                $"Embedding provider '{Name}' returned {payload?.Embeddings?.Count ?? 0} vectors "
                + $"for {inputs.Length} inputs.");
        }

        foreach (var embedding in payload.Embeddings)
        {
            // Caught here rather than at the database, where it would surface as
            // an opaque Postgres type error after the work was already done.
            if (embedding.Length != options.Dimensions)
            {
                throw new EmbeddingException(
                    $"Model '{options.Model}' produced {embedding.Length}-dimension vectors but "
                    + $"Embeddings:Dimensions is {options.Dimensions}. The chunk table is migrated "
                    + "for the configured width, so changing model needs a migration and a re-index.");
            }
        }

        return [.. payload.Embeddings.Select(EmbeddingVector.Normalize)];
    }

    private sealed record OllamaEmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string[] Input,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);

    private sealed record OllamaEmbedResponse(
        [property: JsonPropertyName("embeddings")] IReadOnlyList<float[]>? Embeddings);

    private sealed record OllamaTagsResponse(
        [property: JsonPropertyName("models")] IReadOnlyList<OllamaModel>? Models);

    private sealed record OllamaModel(
        [property: JsonPropertyName("name")] string? Name);
}
