using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations.Llm;

/// <summary>
/// Answers from a local Ollama model.
///
/// Free, key-less, and the question plus the retrieved document text never
/// leaves the machine — which matters more here than for embeddings, since the
/// prompt carries whole passages of internal documentation.
/// </summary>
internal sealed class OllamaLlmProvider(
    HttpClient http,
    IOptions<LlmOptions> options,
    ILogger<OllamaLlmProvider> logger) : ILlmProvider
{
    private readonly LlmOptions options = options.Value;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public string Name => $"ollama/{this.options.Model}";

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        IReadOnlyList<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new OllamaChatRequest(
            options.Model,
            [
                new OllamaChatMessage("system", systemPrompt),
                .. messages.Select(message => new OllamaChatMessage(
                    message.Role == LlmRole.User ? "user" : "assistant",
                    message.Content)),
            ],
            Stream: true,
            new OllamaChatOptions(
                options.Temperature, options.MaxOutputTokens, options.ContextTokens),
            options.KeepAlive);

        using var content = JsonContent.Create(request, options: Json);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = content,
        };

        // ResponseHeadersRead: without it HttpClient buffers the whole body
        // before returning, which defeats the point of streaming entirely.
        using var response = await http.SendAsync(
            message, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Ollama chat call failed with {StatusCode}: {Body}",
                (int)response.StatusCode, body);

            throw new LlmException(
                $"Model provider '{Name}' returned {(int)response.StatusCode}. {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // Ollama streams newline-delimited JSON, one object per fragment,
        // rather than server-sent events.
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;

            OllamaChatResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, Json);
            }
            catch (JsonException exception)
            {
                // A malformed line means the response is no longer trustworthy;
                // better to fail than to hand back half an answer as if whole.
                throw new LlmException(
                    $"Model provider '{Name}' returned an unreadable response fragment.",
                    exception);
            }

            if (chunk?.Message?.Content is { Length: > 0 } fragment)
                yield return fragment;

            if (chunk?.Done == true)
            {
                LogTimings(chunk);
                yield break;
            }
        }
    }

    /// <summary>
    /// Records where an answer's time actually went.
    ///
    /// Without this the only observable fact is that answering took a while.
    /// Reading the prompt and writing the answer have completely different
    /// remedies — fewer passages for one, a smaller model or faster hardware for
    /// the other — and on a box we cannot attach a profiler to, this log is the
    /// only way to tell them apart.
    /// </summary>
    private void LogTimings(OllamaChatResponse done)
    {
        static double Ms(long nanoseconds) => nanoseconds / 1_000_000d;

        // Rate rather than raw duration: tokens per second is comparable across
        // questions of different sizes, which a millisecond total is not.
        var promptRate = done.PromptEvalDuration > 0
            ? done.PromptEvalCount / (done.PromptEvalDuration / 1_000_000_000d)
            : 0;

        logger.LogInformation(
            "Answered with {Provider}: load {LoadMs:F0}ms, prompt {PromptTokens} tokens in "
            + "{PromptMs:F0}ms ({PromptRate:F0} tok/s), generated {OutputTokens} tokens in "
            + "{OutputMs:F0}ms",
            Name,
            Ms(done.LoadDuration),
            done.PromptEvalCount,
            Ms(done.PromptEvalDuration),
            promptRate,
            done.EvalCount,
            Ms(done.EvalDuration));
    }

    public async Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            var tags = await http.GetFromJsonAsync<OllamaTagsResponse>("/api/tags", ct);

            var installed = tags?.Models?
                .Select(model => model.Name ?? string.Empty)
                .ToList() ?? [];

            // Ollama reports "qwen2.5:7b" exactly as pulled, but a model pulled
            // without a tag comes back as ":latest" — compare on the name.
            var hasModel = installed.Any(name =>
                name.Equals(options.Model, StringComparison.OrdinalIgnoreCase) ||
                name.Split(':')[0].Equals(
                    options.Model.Split(':')[0], StringComparison.OrdinalIgnoreCase));

            if (!hasModel)
            {
                return new LlmAvailability(false,
                    $"Ollama is reachable at {options.BaseUrl} but the '{options.Model}' model "
                    + $"is not installed. Run: docker compose exec ollama ollama pull {options.Model}");
            }

            return new LlmAvailability(true, $"Ollama serving '{options.Model}'.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new LlmAvailability(false,
                $"Ollama is not reachable at {options.BaseUrl}. Run: docker compose up -d");
        }
    }

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OllamaChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaChatOptions Options,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);

    private sealed record OllamaChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    /// <param name="NumCtx">
    /// The context window, in tokens.
    ///
    /// Sent explicitly because Ollama's own default is 2048, and it enforces it
    /// by silently discarding the overflow rather than by failing. A grounded
    /// prompt here is the rules, a worked example, and several passages of up
    /// to 800 tokens each — comfortably past that — so leaving it unset means
    /// the model is asked to cite sources it was never shown.
    /// </param>
    private sealed record OllamaChatOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict,
        [property: JsonPropertyName("num_ctx")] int NumCtx);

    /// <param name="LoadDuration">
    /// Nanoseconds. Ollama reports these only on the final fragment, and they
    /// are the difference between "the assistant is slow" and knowing *which*
    /// part is slow — loading the model, reading the prompt, or writing the
    /// answer. Prompt evaluation is usually the largest and is the one that
    /// grows with <c>Chat:PassageCount</c>.
    /// </param>
    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaChatMessage? Message,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("load_duration")] long LoadDuration = 0,
        [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount = 0,
        [property: JsonPropertyName("prompt_eval_duration")] long PromptEvalDuration = 0,
        [property: JsonPropertyName("eval_count")] int EvalCount = 0,
        [property: JsonPropertyName("eval_duration")] long EvalDuration = 0);

    private sealed record OllamaTagsResponse(
        [property: JsonPropertyName("models")] IReadOnlyList<OllamaModel>? Models);

    private sealed record OllamaModel(
        [property: JsonPropertyName("name")] string? Name);
}
