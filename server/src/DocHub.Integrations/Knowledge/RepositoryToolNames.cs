namespace DocHub.Integrations.Knowledge;

/// <summary>
/// How a tool is picked off a server that has not been told which to use.
///
/// Shared by the search path and the "test address" probe on purpose: the probe
/// exists to say what searching *would* do, so a probe that guessed differently
/// from the searcher would be worse than no probe at all.
/// </summary>
internal static class RepositoryToolNames
{
    /// <summary>
    /// The tool a query is put to, or null when nothing looks like one.
    ///
    /// Substring rather than an exact name because MCP does not standardise
    /// this — `search_codebase`, `code_search` and `searchFiles` are all real.
    /// It is still a guess, which is why naming the tool explicitly is the
    /// supported path and this is only the getting-started default.
    /// </summary>
    public static string? PickSearchTool(IEnumerable<string> toolNames) =>
        toolNames.FirstOrDefault(name =>
            name.Contains("search", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The tool that names the repositories a server indexes, or null.
    ///
    /// Only used by the probe, and only to describe what an address is: nothing
    /// grounds an answer in it, so a server without one loses nothing.
    /// </summary>
    public static string? PickRepositoryListTool(IEnumerable<string> toolNames) =>
        toolNames.FirstOrDefault(name =>
            name.Contains("list", StringComparison.OrdinalIgnoreCase)
            && name.Contains("repo", StringComparison.OrdinalIgnoreCase));
}
