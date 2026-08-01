namespace DocHub.DataAccess.Entities;

/// <summary>
/// One MCP repository server, as an administrator added it.
///
/// This table <b>is</b> the list of repository sources — not an override on top
/// of a configured one. Which servers a team searches changes as the team's
/// code moves, and that is operational rather than architectural: it should not
/// need a text editor on the box, an app-pool recycle, and a person who knows
/// where `appsettings.Production.json` lives.
///
/// What stays in configuration is the deployment's decision — whether to search
/// repositories at all, and how many passages to ask each server for. Those
/// change the shape of every answer, so they belong with the deployment.
/// </summary>
public class RepositorySourceSetting
{
    /// <summary>
    /// Stable identifier and primary key. It appears in the API's routes and is
    /// recorded on every citation this server produces, so it is chosen once
    /// and not edited — renaming would orphan the attribution on answers
    /// already given.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>What to call it on screen and in the sentence naming a source that failed.</summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Absolute http/https address of the MCP server. Required: a server with
    /// no address is not a server, and switching one off during an outage is
    /// what <see cref="IsEnabled"/> is for.
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// Which of the server's tools to search with. Empty means "discover it" —
    /// the first tool with "search" in its name, which is a guess worth
    /// replacing once the server's tool list is known.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Whether to search this source at all. Separate from deleting it so an
    /// administrator can take a server out of circulation during an outage
    /// without losing its address and settings.
    /// </summary>
    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedById { get; set; }
}
