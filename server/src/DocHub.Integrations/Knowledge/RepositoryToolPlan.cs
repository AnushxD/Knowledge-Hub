using ModelContextProtocol.Client;

namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Which of a server's tools a question is put to.
///
/// One search tool used to be the whole answer, on the reasoning that only a
/// search tool returns text out of the source and everything else returns the
/// server's own prose. That reasoning still holds for what a citation *means* —
/// see the note on <see cref="McpRepositoryKnowledgeSource"/> — but it turned
/// out to cost real answers: a server whose search tool is thin and whose
/// `get_architecture` tool knows exactly the thing being asked contributed
/// nothing, and the assistant refused a question the server could have
/// grounded. So every tool that can be asked is asked, and the search-shaped
/// ones are simply asked first.
///
/// Shared with the probe, for the same reason the tool-name rule always was:
/// the probe exists to say what searching would do, so it has to use the rule
/// searching uses rather than a second one that agrees most of the time.
///
/// Two things disqualify a tool, and both are worth stating:
///
/// <list type="bullet">
/// <item>
/// <b>It changes something.</b> The hub is read-only over somebody else's
/// repository, and a question routed into <c>delete_branch</c> would break that
/// in the worst possible way. The server's own <c>readOnlyHint</c> decides when
/// it declares one; a name check stands behind it, because most servers declare
/// nothing.
/// </item>
/// <item>
/// <b>The question cannot be put to it.</b> A tool with no string parameter has
/// nowhere to take the query, and one requiring an argument nothing here knows
/// would be called wrong. Either way it would answer the same thing for every
/// question — which is noise on every answer, not grounding.
/// </item>
/// </list>
/// </summary>
internal static class RepositoryToolPlan
{
    /// <summary>
    /// Every tool the question can be put to, search-shaped ones first.
    ///
    /// The order matters beyond neatness: results are interleaved by rank, so
    /// the first tool in this list wins ties, and a tool returning verbatim file
    /// passages should outrank one returning a paragraph it wrote itself.
    /// Within each group the server's own ordering survives, because
    /// <c>OrderByDescending</c> is stable here and the server listing its best
    /// tool first is at least as good a signal as anything available.
    /// </summary>
    public static IReadOnlyList<McpClientTool> Answerable(IEnumerable<McpClientTool> tools) =>
    [
        .. tools
            .Where(CanBeAsked)
            .OrderByDescending(tool => RepositoryToolNames.IsSearchTool(tool.Name)),
    ];

    public static bool CanBeAsked(McpClientTool tool) =>
        !ChangesThings(tool) && TakesAQuestion(tool);

    /// <summary>
    /// The server's declaration first, the name only when it declared nothing.
    ///
    /// A server that says <c>readOnlyHint: true</c> is believed even if it
    /// called the tool <c>update_index</c> — it knows what its tool does and we
    /// are guessing. The reverse is not symmetric: an absent hint means absent,
    /// not safe.
    /// </summary>
    private static bool ChangesThings(McpClientTool tool)
    {
        var annotations = tool.ProtocolTool.Annotations;

        if (annotations?.ReadOnlyHint is { } readOnly) return !readOnly;
        if (annotations?.DestructiveHint is true) return true;

        return RepositoryToolNames.LooksMutating(tool.Name);
    }

    private static bool TakesAQuestion(McpClientTool tool)
    {
        // Mapped against a placeholder rather than the real question: what is
        // being decided here is whether the schema has somewhere to put one, and
        // that is the same answer for every question.
        var mapping = RepositoryToolArguments.Map(tool.ProtocolTool.InputSchema, "probe", 1);

        return mapping.QueryParameter is not null && mapping.UnfilledRequired.Count == 0;
    }
}
