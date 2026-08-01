using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations.Knowledge;

/// <summary>
/// One repository server, as whoever asks for it already knows it.
///
/// Passed in rather than looked up, because the list of servers lives in a
/// database this layer cannot see. Services reads the rows and hands each one
/// over; this layer knows only how to speak MCP to an address.
/// </summary>
/// <param name="Name">
/// Stable identifier, recorded on every citation this server produces.
/// </param>
/// <param name="IsEnabled">
/// False when an administrator has taken it out of circulation. The source is
/// still built and still appears on the sources screen — reporting itself as
/// inactive — because a server switched off on purpose and a server that
/// vanished are different things to look at.
/// </param>
public sealed record RepositorySourceDescriptor(
    string Name,
    string DisplayName,
    string Endpoint,
    string ToolName,
    bool IsEnabled);

/// <summary>
/// Builds a knowledge source for one repository server.
///
/// A factory rather than DI registration because the set of servers is data
/// now, not configuration: it can change between one question and the next, so
/// the sources have to be built per request from whatever the database
/// currently says.
/// </summary>
public interface IRepositoryKnowledgeSourceFactory
{
    IKnowledgeSource Create(RepositorySourceDescriptor source);

    /// <summary>
    /// The stand-in used when there is no server to search, carrying the reason
    /// there isn't one. Built here rather than registered in the container
    /// because whether it is needed depends on a table the container cannot
    /// read.
    /// </summary>
    /// <param name="detail">
    /// One sentence saying why nothing is being searched and what would change
    /// it. This is the whole value of the placeholder — an empty section that
    /// says what would fill it.
    /// </param>
    IKnowledgeSource CreatePlaceholder(string detail);
}

internal sealed class McpRepositoryKnowledgeSourceFactory(
    IOptions<KnowledgeSourceOptions> options,
    ILoggerFactory loggerFactory) : IRepositoryKnowledgeSourceFactory
{
    public IKnowledgeSource Create(RepositorySourceDescriptor source) =>
        new McpRepositoryKnowledgeSource(
            source,
            options,
            loggerFactory,
            loggerFactory.CreateLogger<McpRepositoryKnowledgeSource>());

    public IKnowledgeSource CreatePlaceholder(string detail) =>
        new NullRepositoryKnowledgeSource(detail);
}
