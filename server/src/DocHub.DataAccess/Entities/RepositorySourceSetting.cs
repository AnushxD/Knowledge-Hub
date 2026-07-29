namespace DocHub.DataAccess.Entities;

/// <summary>
/// The repository knowledge source's address, as an administrator set it.
///
/// Configuration still declares the baseline — a deployment that always wants a
/// repository source can say so in `KnowledgeSources:*` and be certain of it.
/// This row is the operational override, so adding or moving an MCP server is
/// something an administrator does in the UI rather than an app-pool
/// environment variable and a recycle.
///
/// Exactly one row, keyed by the source's stable name. A table with one row is
/// a slightly odd shape, but the alternatives are worse: a general key/value
/// settings table loses every type and constraint, and a second column on some
/// unrelated entity hides what this is.
/// </summary>
public class RepositorySourceSetting
{
    /// <summary>The source's stable name — "repositories". Also the primary key.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Absolute http/https address of the MCP server. Null means "not set here",
    /// which falls back to configuration rather than meaning "disabled".
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Whether to search this source at all. Separate from
    /// <see cref="Endpoint"/> so an administrator can switch a source off during
    /// an outage without losing the address they will want back.
    /// </summary>
    public bool IsEnabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who last changed it. Null for a row that has only ever been the default.</summary>
    public Guid? UpdatedById { get; set; }
}
