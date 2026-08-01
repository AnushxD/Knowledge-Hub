namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Where each repository source currently points, and whether to search it.
///
/// Defined here rather than in Services for the same reason
/// <see cref="IKnowledgeSource"/> is: the client that consumes it lives in this
/// layer, and Services references Integrations and never the reverse. The
/// implementation lives in Services, because the answer comes from a database
/// row this layer cannot see.
///
/// Resolved per request, not captured at startup. That is the whole point —
/// an administrator changing an address in the UI must take effect on the next
/// question, not on the next application pool recycle.
/// </summary>
public interface IRepositorySourceSettings
{
    /// <summary>
    /// The effective settings for one declared source, or null when no source
    /// goes by that name. Unknown is distinct from unconfigured: a source
    /// configuration never declared cannot be overridden into existence.
    /// </summary>
    Task<RepositorySourceState?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Every declared source, in the order configuration lists them.</summary>
    Task<IReadOnlyList<RepositorySourceState>> ListAsync(CancellationToken ct = default);
}

/// <summary>The effective settings, after the override and configuration are reconciled.</summary>
/// <param name="Name">The stable identifier configuration declared it under.</param>
/// <param name="DisplayName">What to call it on screen.</param>
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
/// True when the value came from `KnowledgeSources:Repositories` rather than
/// from the database. Surfaced so the sources screen can say which one is in
/// effect — otherwise an administrator editing a field that configuration is
/// overriding has no way to tell why nothing changed.
/// </param>
/// <param name="ConfiguredEndpoint">
/// What clearing the override would fall back to, which is not always an
/// address: null means configuration declares none, or the provider is "none",
/// and clearing would switch the source off rather than restore anything.
/// Carried so the UI can say which of those "Use configuration" will do instead
/// of looking like it destroys the address either way.
/// </param>
public sealed record RepositorySourceState(
    string Name,
    string DisplayName,
    string? Endpoint,
    bool IsEnabled,
    bool IsFromConfiguration,
    string? ConfiguredEndpoint);
