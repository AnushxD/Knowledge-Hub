using System.Text.Json;
using DocHub.Integrations.Knowledge;

namespace DocHub.Integrations.Tests;

/// <summary>
/// Working out what a search tool calls its arguments.
///
/// These are the shapes real servers actually publish. The one that prompted
/// them takes <c>q</c> and no limit at all; sending it <c>query</c> and
/// <c>maxResults</c> got a search for the empty string back, which then went to
/// the model as grounding.
/// </summary>
public sealed class RepositoryToolArgumentsTests
{
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void The_documented_convention_still_maps_to_itself()
    {
        var mapping = RepositoryToolArguments.Map(
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string" },
                    "maxResults": { "type": "integer" }
                  },
                  "required": ["query"]
                }
                """),
            "restart the worker",
            6);

        Assert.Equal("query", mapping.QueryParameter);
        Assert.Equal("maxResults", mapping.LimitParameter);
        Assert.Equal("restart the worker", mapping.Arguments["query"]);
        Assert.Equal(6, mapping.Arguments["maxResults"]);
    }

    [Fact]
    public void A_tool_that_calls_its_query_q_is_sent_q()
    {
        // The live server that exposed the bug: q is required, there is no
        // limit, and the extra filters are none of our business.
        var mapping = RepositoryToolArguments.Map(
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "country": { "type": "string" },
                    "language": { "type": "string" },
                    "q": { "type": "string" }
                  },
                  "required": ["q"]
                }
                """),
            "Arsenal",
            6);

        Assert.Equal("q", mapping.QueryParameter);
        Assert.Equal("Arsenal", mapping.Arguments["q"]);

        // No limit parameter exists, so none is sent. Inventing "maxResults"
        // here is exactly what made the server ignore the whole call.
        Assert.Null(mapping.LimitParameter);
        Assert.Single(mapping.Arguments);
    }

    [Fact]
    public void A_query_parameter_with_an_unguessable_name_is_found_by_being_the_required_one()
    {
        // The name list cannot cover every server. One required string is an
        // unambiguous answer whatever it is called.
        var mapping = RepositoryToolArguments.Map(
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "needle": { "type": "string" },
                    "haystack": { "type": "string" }
                  },
                  "required": ["needle"]
                }
                """),
            "Arsenal",
            6);

        Assert.Equal("needle", mapping.QueryParameter);
    }

    [Fact]
    public void Two_required_strings_are_left_alone_and_reported()
    {
        // Picking one at random would send the query to the wrong field and
        // look like it worked. Better to fall back and say what is missing.
        var mapping = RepositoryToolArguments.Map(
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "repo": { "type": "string" },
                    "pattern": { "type": "string" }
                  },
                  "required": ["repo", "pattern"]
                }
                """),
            "Arsenal",
            6);

        Assert.Null(mapping.QueryParameter);
        Assert.Equal(["repo", "pattern"], mapping.UnfilledRequired);
    }

    [Fact]
    public void An_explicit_query_wins_over_a_bare_q()
    {
        var mapping = RepositoryToolArguments.Map(
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "q": { "type": "string" },
                    "query": { "type": "string" }
                  }
                }
                """),
            "Arsenal",
            6);

        Assert.Equal("query", mapping.QueryParameter);
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("top_k")]
    [InlineData("topK")]
    [InlineData("count")]
    public void A_limit_is_recognised_whatever_it_is_called(string name)
    {
        var mapping = RepositoryToolArguments.Map(
            Schema($$"""
                {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string" },
                    "{{name}}": { "type": "integer" }
                  }
                }
                """),
            "Arsenal",
            6);

        Assert.Equal(name, mapping.LimitParameter);
        Assert.Equal(6, mapping.Arguments[name]);
    }

    [Fact]
    public void A_tool_with_no_usable_schema_falls_back_to_the_old_convention()
    {
        // No worse off than before this existed, and the caller is told so it
        // can log why this source may return nothing.
        var mapping = RepositoryToolArguments.Map(Schema("""{ "type": "object" }"""), "Arsenal", 6);

        Assert.Null(mapping.QueryParameter);
        Assert.Equal("Arsenal", mapping.Arguments["query"]);
    }

    [Fact]
    public void A_nullable_string_still_counts_as_a_string()
    {
        var mapping = RepositoryToolArguments.Map(
            Schema("""
                {
                  "type": "object",
                  "properties": { "q": { "type": ["string", "null"] } },
                  "required": ["q"]
                }
                """),
            "Arsenal",
            6);

        Assert.Equal("q", mapping.QueryParameter);
    }
}
