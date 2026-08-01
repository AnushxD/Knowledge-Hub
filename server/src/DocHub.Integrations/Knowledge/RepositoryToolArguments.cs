using System.Text.Json;

namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Works out what to call a search tool's arguments, from the tool's own schema.
///
/// MCP standardises how a tool <i>describes</i> its inputs and says nothing
/// about what they should be called. Real servers differ: one takes
/// <c>query</c> and <c>maxResults</c>, another takes <c>q</c> with a country
/// filter and no limit at all. Hard-coding one convention means the other
/// server silently receives no query — it does not fail, it searches for the
/// empty string and returns something useless, which is then offered to the
/// model as grounding.
///
/// So the schema is read rather than assumed. Names are only a tie-breaker
/// among candidates the schema already says are acceptable.
/// </summary>
internal static class RepositoryToolArguments
{
    /// <summary>
    /// Names a query parameter goes by, best first. Ordered by how unambiguous
    /// they are, not by how common: <c>q</c> is common and could be anything,
    /// so an explicit <c>query</c> wins when a tool offers both.
    /// </summary>
    private static readonly string[] QueryNames =
        ["query", "q", "searchterm", "search_term", "search", "term", "text", "keywords", "prompt"];

    /// <summary>Names a result limit goes by, best first.</summary>
    private static readonly string[] LimitNames =
        ["maxresults", "max_results", "limit", "topk", "top_k", "count", "max", "size", "n"];

    /// <summary>
    /// The arguments to send, plus any required parameter that could not be
    /// filled — which is the caller's warning that this tool wants something
    /// only a human knows.
    /// </summary>
    /// <param name="QueryParameter">
    /// Null when the schema offered nothing string-shaped. The query is sent as
    /// <c>query</c> anyway in that case: a tool that publishes no usable schema
    /// is no worse off under the old convention than under no convention.
    /// </param>
    public sealed record Mapping(
        Dictionary<string, object?> Arguments,
        string? QueryParameter,
        string? LimitParameter,
        IReadOnlyList<string> UnfilledRequired);

    public static Mapping Map(JsonElement? schema, string query, int take)
    {
        var properties = PropertiesOf(schema);
        var required = RequiredOf(schema);

        var queryParameter = Pick(properties, QueryNames, "string", required);
        var limitParameter = Pick(properties, LimitNames, "integer", required)
            ?? Pick(properties, LimitNames, "number", required);

        var arguments = new Dictionary<string, object?>
        {
            [queryParameter ?? "query"] = query,
        };

        // Omitted rather than guessed when the tool does not offer one: sending
        // an unknown argument is what caused this to be written.
        if (limitParameter is not null) arguments[limitParameter] = take;

        var unfilled = required
            .Where(name => !arguments.ContainsKey(name))
            .ToList();

        return new Mapping(arguments, queryParameter, limitParameter, unfilled);
    }

    /// <summary>
    /// A parameter of the wanted type, preferring the recognised names and
    /// falling back to the only required one of that type.
    ///
    /// The fallback matters more than the name list: a tool with exactly one
    /// required string parameter has told us where the query goes, whatever it
    /// chose to call it.
    /// </summary>
    private static string? Pick(
        IReadOnlyDictionary<string, string> properties,
        string[] preferred,
        string wantedType,
        IReadOnlyList<string> required)
    {
        var ofType = properties
            .Where(property => property.Value == wantedType)
            .Select(property => property.Key)
            .ToList();

        foreach (var name in preferred)
        {
            var match = ofType.FirstOrDefault(candidate =>
                candidate.Replace("_", string.Empty)
                    .Equals(name.Replace("_", string.Empty), StringComparison.OrdinalIgnoreCase));

            if (match is not null) return match;
        }

        var requiredOfType = ofType.Where(required.Contains).ToList();

        return requiredOfType.Count == 1 ? requiredOfType[0] : null;
    }

    /// <summary>Property name to JSON type, for the properties that declare one.</summary>
    private static IReadOnlyDictionary<string, string> PropertiesOf(JsonElement? schema)
    {
        var properties = new Dictionary<string, string>();

        if (schema is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("properties", out var declared)
            || declared.ValueKind != JsonValueKind.Object)
        {
            return properties;
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;

            // A union like ["string", "null"] counts as its first named type,
            // which is what a caller would send anyway.
            if (property.Value.TryGetProperty("type", out var type))
            {
                var name = type.ValueKind switch
                {
                    JsonValueKind.String => type.GetString(),
                    JsonValueKind.Array => type.EnumerateArray()
                        .FirstOrDefault(entry => entry.ValueKind == JsonValueKind.String)
                        .GetString(),
                    _ => null,
                };

                if (name is not null) properties[property.Name] = name;
            }
        }

        return properties;
    }

    private static IReadOnlyList<string> RequiredOf(JsonElement? schema)
    {
        if (schema is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("required", out var required)
            || required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. required.EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.String)
                .Select(entry => entry.GetString()!),
        ];
    }
}
