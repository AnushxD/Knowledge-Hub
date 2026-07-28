using DocHub.DataAccess.Dtos;

namespace DocHub.DataAccess.Repositories;

/// <summary>Persistence for conversations with the assistant.</summary>
public interface IChatRepository
{
    Task<ChatSessionDto> CreateSessionAsync(
        Guid userId,
        string title,
        CancellationToken ct = default);

    /// <summary>A session with its full transcript, or null when unknown.</summary>
    Task<ChatTranscriptDto?> GetTranscriptAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>A user's sessions, most recently used first.</summary>
    Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(
        Guid userId,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Appends a message and touches the session's UpdatedAt, so history stays
    /// ordered by activity rather than by creation.
    /// </summary>
    Task<ChatMessageDto?> AppendMessageAsync(
        Guid sessionId,
        NewChatMessageDto message,
        CancellationToken ct = default);

    Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);
}
