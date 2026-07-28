namespace DocHub.DataAccess.Entities;

/// <summary>
/// One conversation with the assistant.
///
/// Sessions are persisted rather than kept in memory because a grounded answer
/// is only auditable if the question, the answer and the sources it cited are
/// all still there to look at later.
/// </summary>
public class ChatSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// Derived from the first question, so the history list reads as a list of
    /// questions rather than of timestamps.
    /// </summary>
    public required string Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the last message was appended; orders the history list.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = [];
}
