namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Where the repository source currently points, and whether to search it.
///
/// Defined here rather than in Services for the same reason
/// <see cref="IKnowledgeSource"/> is: the client that consumes it lives in this
/// layer, and Services references Integrations and never the reverse. The
/// implementation lives in Services, because the answer comes from a database
/// row this layer cannot see.
///
/// Resolved per request, not captured at startup. That is the whole point —
/// an administrator changing the address in the UI must take effect on the next
/// question, not on the next application pool recycle.
/// </summary>
public interface IRepositorySourceSettings
{
    Task<RepositorySourceState> GetAsync(CancellationToken ct = default);
}

/// <summary>The effective settings, after the override and configuration are reconciled.</summary>
/// <param name="Endpoint">
/// Null when no usable address is configured anywhere. A source with no address
/// is inactive, not broken.
/// </param>
/// <param name="IsEnabled">
/// False when an administrator switched the source off. Kept separate from a
/// missing endpoint so switching a source off during an outage does not throw
/// away the address it will need back.
/// </param>
/// <param name="IsFromConfiguration">
/// True when the value came from `KnowledgeSources:*` rather than from the
/// database. Surfaced so the sources screen can say which one is in effect —
/// otherwise an administrator editing a field that configuration is overriding
/// has no way to tell why nothing changed.
/// </param>
public sealed record RepositorySourceState(
    string? Endpoint,
    bool IsEnabled,
    bool IsFromConfiguration);
