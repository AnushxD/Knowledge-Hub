namespace DocHub.Integrations.Knowledge;

/// <summary>
/// What a tool's name suggests about it.
///
/// Shared by the search path and the "test address" probe on purpose: the probe
/// exists to say what searching *would* do, so a probe that guessed differently
/// from the searcher would be worse than no probe at all.
/// </summary>
internal static class RepositoryToolNames
{
    /// <summary>
    /// Whether this looks like a search tool — one that answers with passages
    /// out of the source rather than with prose of its own.
    ///
    /// Substring rather than an exact name because MCP does not standardise
    /// this — `search_codebase`, `code_search` and `searchFiles` are all real.
    /// Nothing is excluded on the strength of this: it decides ordering, so the
    /// tools most likely to return citable text are asked first.
    /// </summary>
    public static bool IsSearchTool(string name) =>
        name.Contains("search", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The tool a query is put to first, or null when nothing looks like one.
    ///
    /// Only the probe needs a single answer now — it is what the screen offers
    /// to pin the source to. Searching asks every tool it can.
    /// </summary>
    public static string? PickSearchTool(IEnumerable<string> toolNames) =>
        toolNames.FirstOrDefault(IsSearchTool);

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

    /// <summary>
    /// Whether the name says this tool changes something.
    ///
    /// A backstop behind the server's own <c>readOnlyHint</c>, which is optional
    /// and widely omitted. Asking every tool a question means a tool nobody
    /// vetted can be called with the user's words in it, and
    /// <c>delete_branch(query: "how do we restart the worker")</c> is not a
    /// search that returns nothing — it is a write into somebody's repository.
    ///
    /// Deliberately blunt, and deliberately biased towards excluding: a read
    /// tool wrongly skipped costs one source of passages, and it can still be
    /// named explicitly on the source, while a write tool wrongly called cannot
    /// be undone from here. The hub never writes to a repository, and this is
    /// that rule holding for servers whose tools we have never seen.
    /// </summary>
    public static bool LooksMutating(string name)
    {
        foreach (var verb in MutatingVerbs)
        {
            if (name.Contains(verb, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Substrings rather than prefixes: `repo_delete` and `deleteRepo` are the
    /// same tool, and only one of them starts with the verb.
    /// </summary>
    private static readonly string[] MutatingVerbs =
    [
        "create", "update", "delete", "remove", "write", "insert", "drop", "add",
        "set_", "put", "patch", "post", "edit", "modify", "rename", "move", "copy",
        "merge", "push", "commit", "revert", "reset", "upload", "publish", "deploy",
        "execute", "exec", "run_", "invoke", "send", "close", "approve", "assign",
        "purge", "prune", "clear", "truncate", "cancel", "restart", "trigger",
        "archive", "enable", "disable", "grant", "revoke", "lock", "sync",
    ];
}
