using System.Linq.Expressions;
using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Repositories;

internal sealed class ChatRepository(DocHubDbContext db) : IChatRepository
{
    public async Task<ChatSessionDto> CreateSessionAsync(
        Guid userId,
        string title,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var session = new ChatSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Title = title,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.ChatSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return new ChatSessionDto(session.Id, session.Title, 0, session.CreatedAt, session.UpdatedAt);
    }

    public async Task<ChatTranscriptDto?> GetTranscriptAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var session = await db.ChatSessions
            .AsNoTracking()
            .Where(candidate => candidate.Id == sessionId)
            .Select(SessionProjection)
            .FirstOrDefaultAsync(ct);

        if (session is null) return null;

        var messages = await db.ChatMessages
            .AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .OrderBy(message => message.CreatedAt)
            .Select(MessageProjection)
            .ToListAsync(ct);

        return new ChatTranscriptDto(session, messages);
    }

    public async Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(
        Guid userId,
        int take = 50,
        CancellationToken ct = default) =>
        await db.ChatSessions
            .AsNoTracking()
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.UpdatedAt)
            .Take(take)
            .Select(SessionProjection)
            .ToListAsync(ct);

    public async Task<ChatMessageDto?> AppendMessageAsync(
        Guid sessionId,
        NewChatMessageDto input,
        CancellationToken ct = default)
    {
        var session = await db.ChatSessions
            .FirstOrDefaultAsync(candidate => candidate.Id == sessionId, ct);

        if (session is null) return null;

        var now = DateTimeOffset.UtcNow;

        var message = new ChatMessage
        {
            Id = Guid.CreateVersion7(),
            SessionId = sessionId,
            Role = input.Role,
            Content = input.Content,
            Citations = [.. input.Citations],
            IsRefusal = input.IsRefusal,
            Degradations = [.. input.Degradations],
            CreatedAt = now,
        };

        db.ChatMessages.Add(message);

        // Keeps the history list ordered by activity rather than by creation.
        session.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        return new ChatMessageDto(
            message.Id,
            message.SessionId,
            message.Role,
            message.Content,
            message.Citations,
            message.IsRefusal,
            message.CreatedAt,
            message.Degradations);
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.ChatSessions
            .Where(session => session.Id == sessionId)
            .ExecuteDeleteAsync(ct) > 0;

    private static readonly Expression<Func<ChatSession, ChatSessionDto>> SessionProjection =
        session => new ChatSessionDto(
            session.Id,
            session.Title,
            session.Messages.Count,
            session.CreatedAt,
            session.UpdatedAt);

    private static readonly Expression<Func<ChatMessage, ChatMessageDto>> MessageProjection =
        message => new ChatMessageDto(
            message.Id,
            message.SessionId,
            message.Role,
            message.Content,
            message.Citations,
            message.IsRefusal,
            message.CreatedAt,
            message.Degradations);
}
