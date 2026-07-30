using DocHub.Services.Chat;
using DocHub.Services.Search;

namespace DocHub.Services.Tests;

/// <summary>
/// The grounding rules and, more importantly, the citation check. This is the
/// logic that decides whether an answer's sources can be trusted, and it needs
/// no model or database to exercise — so it is tested directly rather than by
/// observing what a model happens to do.
/// </summary>
public sealed class GroundedPromptTests
{
    private static RetrievedPassage Passage(string title, string heading, int chunkId = 0) =>
        new(
            PassageKind.Document,
            title,
            chunkId,
            heading,
            $"Body of {heading}.",
            0.5,
            "both",
            DocumentId: Guid.NewGuid(),
            SourceName: "documents");

    private static readonly IReadOnlyList<RetrievedPassage> ThreeSources =
    [
        Passage("VPN Guide", "Connecting"),
        Passage("VPN Guide", "Multi-factor", 1),
        Passage("Runbook", "Escalation", 4),
    ];

    [Fact]
    public void Every_passage_is_numbered_for_the_model_to_cite()
    {
        var prompt = GroundedPrompt.Build(ThreeSources);

        Assert.Contains("[1] VPN Guide — Connecting", prompt);
        Assert.Contains("[2] VPN Guide — Multi-factor", prompt);
        Assert.Contains("[3] Runbook — Escalation", prompt);

        // The refusal wording has to reach the model verbatim, or the phrase
        // the client recognises and the phrase the model produces drift apart.
        Assert.Contains(GroundedPrompt.RefusalPhrase, prompt);
    }

    [Fact]
    public void Markers_resolve_to_the_passage_they_point_at()
    {
        var citations = GroundedPrompt.VerifyCitations(
            "Use the client [1]. Escalate after thirty minutes [3].", ThreeSources);

        Assert.Equal([1, 3], citations.Select(citation => citation.Marker));
        Assert.Equal("Connecting", citations[0].Heading);
        Assert.Equal("Escalation", citations[1].Heading);
        Assert.Equal(ThreeSources[2].DocumentId, citations[1].DocumentId);
    }

    [Fact]
    public void A_marker_beyond_the_supplied_sources_is_discarded()
    {
        // The failure this whole check exists for: a model asked to cite will
        // sometimes produce a plausible number it was never given.
        var citations = GroundedPrompt.VerifyCitations(
            "Rotate the key every ninety days [7].", ThreeSources);

        Assert.Empty(citations);
    }

    [Fact]
    public void Zero_and_negative_markers_are_discarded()
    {
        var citations = GroundedPrompt.VerifyCitations("Something [0]. Else [00].", ThreeSources);

        Assert.Empty(citations);
    }

    [Fact]
    public void A_marker_repeated_across_sentences_is_listed_once()
    {
        var citations = GroundedPrompt.VerifyCitations(
            "First point [1]. Second point [1]. Third [1].", ThreeSources);

        Assert.Single(citations);
    }

    [Fact]
    public void Citations_are_ordered_by_marker_regardless_of_where_they_appear()
    {
        var citations = GroundedPrompt.VerifyCitations(
            "Later source first [3]. Earlier one after [1].", ThreeSources);

        Assert.Equal([1, 3], citations.Select(citation => citation.Marker));
    }

    [Fact]
    public void Unresolved_markers_are_stripped_from_the_rendered_answer()
    {
        const string Answer = "Grounded claim [1]. Invented claim [9].";

        var citations = GroundedPrompt.VerifyCitations(Answer, ThreeSources);
        var cleaned = GroundedPrompt.StripUnresolvedMarkers(Answer, citations);

        // The real one survives so the reader can follow it; the invented one
        // goes, rather than rendering as a link to nothing.
        Assert.Contains("[1]", cleaned);
        Assert.DoesNotContain("[9]", cleaned);
        Assert.Contains("Invented claim", cleaned);
    }

    [Theory]
    [InlineData("I don't have information about that in the indexed documents.")]
    [InlineData("I do not have information about that in the indexed documents.")]
    [InlineData("Sorry, but I don't have information about that in these documents.")]
    public void The_refusal_phrase_is_recognised_even_when_worded_loosely(string answer)
    {
        // A small model reproduces the sentence closely but rarely exactly, and
        // a missed refusal renders as an ordinary answer with no citations.
        Assert.True(GroundedPrompt.IsRefusal(answer));
    }

    [Fact]
    public void A_real_answer_is_not_mistaken_for_a_refusal()
    {
        Assert.False(GroundedPrompt.IsRefusal(
            "The VPN gateway is vpn.example-corp.internal [1]."));
    }

    [Fact]
    public void Bracketed_text_that_is_not_a_number_is_ignored()
    {
        var citations = GroundedPrompt.VerifyCitations(
            "See the appendix [see note] and the guide [1].", ThreeSources);

        Assert.Single(citations);
        Assert.Equal(1, citations[0].Marker);
    }
}
