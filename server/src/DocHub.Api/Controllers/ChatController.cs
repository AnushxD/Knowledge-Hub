using System.Text.Json;
using DocHub.Services.Chat;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Produces("application/json")]
public sealed class ChatController(IChatService chat) : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Asks a question and streams the grounded answer back as server-sent
    /// events.
    ///
    /// Event names are meaningful — <c>session</c>, <c>sources</c>,
    /// <c>token</c>, <c>done</c>, <c>error</c> — so the client can render
    /// retrieved sources while the answer is still being written.
    /// </summary>
    [HttpPost]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        await using var events = chat.AskAsync(request, ct).GetAsyncEnumerator(ct);

        // The first event is pulled *before* any bytes are written, so a
        // validation failure or an unknown session still comes back as a normal
        // problem-details response. Once the stream has started the status code
        // is already sent and every error has to be reported inside it.
        if (!await events.MoveNextAsync()) return;

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        // Proxies buffer by default, which would hold the whole answer back and
        // deliver it in one lump — exactly what streaming exists to avoid.
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        do
        {
            await WriteEventAsync(events.Current, ct);
        }
        while (!ct.IsCancellationRequested && await events.MoveNextAsync());
    }

    /// <summary>The current user's conversations, most recently used first.</summary>
    [HttpGet("sessions")]
    [ProducesResponseType<IReadOnlyList<ChatSessionViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ChatSessionViewModel>> Sessions(CancellationToken ct) =>
        await chat.ListSessionsAsync(ct);

    /// <summary>One conversation with its full transcript and citations.</summary>
    [HttpGet("sessions/{id:guid}")]
    [ProducesResponseType<ChatTranscriptViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ChatTranscriptViewModel> Transcript(Guid id, CancellationToken ct) =>
        await chat.GetTranscriptAsync(id, ct);

    [HttpDelete("sessions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await chat.DeleteSessionAsync(id, ct);
        return NoContent();
    }

    private async Task WriteEventAsync(ChatEvent @event, CancellationToken ct)
    {
        var (name, payload) = @event switch
        {
            ChatEvent.SessionOpened opened =>
                ("session", (object)new { sessionId = opened.SessionId, title = opened.Title }),
            ChatEvent.SourcesRetrieved sources =>
                ("sources", new { sources = sources.Sources }),
            ChatEvent.Token token =>
                ("token", new { text = token.Text }),
            ChatEvent.Completed completed =>
                ("done", new
                {
                    messageId = completed.MessageId,
                    citations = completed.Citations,
                    isRefusal = completed.IsRefusal,
                }),
            ChatEvent.Failed failed =>
                ("error", new { reason = failed.Reason }),
            _ => ("token", new { text = string.Empty }),
        };

        await Response.WriteAsync($"event: {name}\n", ct);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, StreamJson)}\n\n", ct);

        // Without an explicit flush the fragments sit in the response buffer
        // and arrive together at the end.
        await Response.Body.FlushAsync(ct);
    }
}
