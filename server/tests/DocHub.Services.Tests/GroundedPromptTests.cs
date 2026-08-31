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
    [Fact]
    public void A_quoted_path_must_appear_in_the_passage_it_cites()
    {
        var passages = new[]
        {
            Passage(
                "fine_grained_access_tokens_rest",
                "Cluster Agent",
                """
                Grants the ability to create, delete, and read cluster agents.
                | Create | Project | `POST` | `/projects/:id/cluster_agents` |
                """),
        };

        // The path is real and the words around it overlap heavily — "cluster",
        // "agent", "projects" — so word counting alone accepts this. No passage
        // the model was given contains the endpoint it quoted, though, so there
        // is nothing to point the citation at and nothing grounding the claim.
        var citations = GroundedPrompt.VerifyCitations(
            "- `POST /projects/:id/cluster_agents/:agent_id/tokens` [1]",
            passages,
            "how do I create a cluster agent token?");

        Assert.Empty(citations);
    }

    [Fact]
    public void A_misattributed_path_is_repointed_at_the_passage_that_has_it()
    {
        var passages = new[]
        {
            Passage(
                "fine_grained_access_tokens_rest",
                "Cluster Agent",
                "Grants the ability to create, delete, and read cluster agents. "
                + "| Create | Project | `POST` | `/projects/:id/cluster_agents` |"),
            Passage(
                "fine_grained_access_tokens_rest",
                "Cluster Agent Token",
                "Grants the ability to create, read, and revoke cluster agent tokens. "
                + "| Create | Project | `POST` | `/projects/:id/cluster_agents/:agent_id/tokens` |",
                chunkId: 22),
        };

        // The model quoted the right path and marked it against the wrong
        // section. Refusing would throw away a true answer; leaving it as
        // written would send a reader somewhere the path is not.
        var citation = Assert.Single(GroundedPrompt.VerifyCitations(
            "- `POST /projects/:id/cluster_agents/:agent_id/tokens` [1]",
            passages,
            "how do I create a cluster agent token?"));

        Assert.Equal("Cluster Agent Token", citation.Heading);
        Assert.Equal(22, citation.ChunkId);

        // The number in the text is what the reader clicks, so it stays as the
        // model wrote it — only where it points has been corrected.
        Assert.Equal(1, citation.Marker);
    }

    [Fact]
    public void One_passage_is_not_cited_twice_because_two_markers_were_wrong()
    {
        var passages = new[]
        {
            Passage(
                "fine_grained_access_tokens_rest",
                "Cluster Agent",
                "Grants the ability to create, delete, and read cluster agents. "
                + "| Create | Project | `POST` | `/projects/:id/cluster_agents` |"),
            Passage(
                "fine_grained_access_tokens_rest",
                "Cluster Agent Token",
                "Grants the ability to create, read, and revoke cluster agent tokens. "
                + "| Create | Project | `POST` | `/projects/:id/cluster_agents/:agent_id/tokens` |",
                chunkId: 22),
        };

        // [1] is wrong and would be corrected onto the section [2] already
        // cites correctly. One claim, one source: the corrected marker gives
        // way rather than rendering as corroboration, and the number the model
        // got right is the one the reader keeps.
        var citation = Assert.Single(GroundedPrompt.VerifyCitations(
            "- `POST /projects/:id/cluster_agents/:agent_id/tokens` [1][2]",
            passages,
            "how do I create a cluster agent token?"));

        Assert.Equal("Cluster Agent Token", citation.Heading);
        Assert.Equal(2, citation.Marker);
    }

    [Fact]
    public void A_quoted_path_that_is_in_the_passage_is_cited()
    {
        var passages = new[]
        {
            Passage(
                "fine_grained_access_tokens_rest",
                "Activity Analytics",
                """
                Grants the ability to read activity analytics.
                | Read | Group | `GET` | `/analytics/group_activity/issues_count` |
                """),
        };

        // The list line the prompt now asks for: short, almost no prose, and
        // verified by the one thing on it that matters.
        var citations = GroundedPrompt.VerifyCitations(
            "- `/analytics/group_activity/issues_count` [1]",
            passages,
            "can you specify the paths?");

        Assert.Equal("Activity Analytics", Assert.Single(citations).Heading);
    }

    /// <param name="body">
    /// Real prose, not a placeholder: a citation is now checked against the
    /// words of the passage it points at, so a fixture whose body said nothing
    /// would be testing the check rather than the behaviour under it.
    /// </param>
    private static RetrievedPassage Passage(
        string title,
        string heading,
        string body,
        int chunkId = 0) =>
        new(
            PassageKind.Document,
            title,
            chunkId,
            heading,
            body,
            0.5,
            "both",
            DocumentId: Guid.NewGuid(),
            SourceName: "documents");

    private static readonly IReadOnlyList<RetrievedPassage> ThreeSources =
    [
        Passage(
            "VPN Guide",
            "Connecting",
            "Download the client from the IT portal and sign in with your network credentials."),
        Passage(
            "VPN Guide",
            "Multi-factor",
            "Every remote session requires a second factor from the authenticator app.",
            1),
        Passage(
            "Runbook",
            "Escalation",
            "Escalate to the on-call engineer after thirty minutes without acknowledgement.",
            4),
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
            "Download the client from the portal [1]. Escalate after thirty minutes [3].",
            ThreeSources);

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
            "Download the client from the portal [7].", ThreeSources);

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
            """
            Download the client from the portal [1]. Sign in with your network
            credentials [1]. The portal client is the only one supported [1].
            """,
            ThreeSources);

        Assert.Single(citations);
    }

    [Fact]
    public void Citations_are_ordered_by_marker_regardless_of_where_they_appear()
    {
        var citations = GroundedPrompt.VerifyCitations(
            """
            Escalate to the on-call engineer after thirty minutes [3]. Download the
            client from the IT portal first [1].
            """,
            ThreeSources);

        Assert.Equal([1, 3], citations.Select(citation => citation.Marker));
    }

    [Fact]
    public void Unresolved_markers_are_stripped_from_the_rendered_answer()
    {
        const string Answer =
            "Download the client from the portal [1]. Rotate the signing key yearly [9].";

        var citations = GroundedPrompt.VerifyCitations(Answer, ThreeSources);
        var cleaned = GroundedPrompt.StripUnresolvedMarkers(Answer, citations);

        // The real one survives so the reader can follow it; the invented one
        // goes, rather than rendering as a link to nothing.
        Assert.Contains("[1]", cleaned);
        Assert.DoesNotContain("[9]", cleaned);
        Assert.Contains("Rotate the signing key yearly", cleaned);
    }

    // ---- citations that point somewhere real and mean nothing ---------------
    //
    // These are the case that got through: an orange-juice recipe, cited to a
    // realtime status probe and a payments data model, because the markers
    // resolved. The passages below are the ones actually retrieved.

    private static readonly IReadOnlyList<RetrievedPassage> UnrelatedSources =
    [
        Passage(
            "zz-realtime",
            "Realtime status probe",
            "Uploaded to watch the status change without a reload. The ingestion worker "
            + "should take it from Queued to Indexed."),
        Passage(
            "auth_workflows",
            "6. OAuth 2.0 / SSO Login Flow",
            "User clicks sign in with provider, the app redirects to the identity provider, "
            + "the user authenticates and grants consent.",
            1),
    ];

    [Fact]
    public void A_citation_whose_passage_says_nothing_about_the_claim_is_dropped()
    {
        var citations = GroundedPrompt.VerifyCitations(
            "The resulting juice can be strained through a fine-mesh sieve to remove pulp [1].",
            UnrelatedSources,
            "how to make orange juice");

        // The marker resolves. The passage is about an ingestion worker. A
        // citation is a promise that the passage backs the sentence.
        Assert.Empty(citations);
    }

    [Fact]
    public void A_citation_justified_only_by_the_question_being_echoed_back_is_dropped()
    {
        // One configured server answers every search with "Search results for
        // '…'", so its passage always contains the question's own words. That
        // must not read as agreement with whatever the model then invented.
        IReadOnlyList<RetrievedPassage> echoing =
        [
            Passage(
                "Live score",
                "result",
                "Search results for 'how to make orange juice': {\"result\": []}"),
        ];

        var citations = GroundedPrompt.VerifyCitations(
            "To make orange juice, peel the oranges and squeeze them with a juicer [1].",
            echoing,
            "how to make orange juice");

        Assert.Empty(citations);
    }

    [Fact]
    public void A_citation_backed_by_words_the_question_never_supplied_survives()
    {
        // The guard must not swallow the ordinary case: the answer carries
        // detail that could only have come from the passage.
        var citations = GroundedPrompt.VerifyCitations(
            "Download the client from the IT portal and sign in with your network credentials [1].",
            ThreeSources,
            "how do I connect to the VPN");

        Assert.Single(citations);
    }

    [Fact]
    public void A_sentence_too_short_to_judge_keeps_its_citation()
    {
        // Nothing to weigh. Refusing over "Yes [1]." would be the check
        // inventing a problem rather than catching one.
        var citations = GroundedPrompt.VerifyCitations("Yes [1].", ThreeSources, "is it on?");

        Assert.Single(citations);
    }

    [Fact]
    public void An_invented_footnote_does_not_count_as_a_verified_citation()
    {
        // Measured against qwen2.5:7b, asked "arsenal": it answered from its own
        // training and closed with a footnote of its own invention. The trailing
        // marker fell after the exclamation mark, so the sentence it was checked
        // against was the empty span between them — nothing to weigh, and a
        // citation with nothing to weigh was kept. That single approved citation
        // was enough to defeat the refusal, and the fabrication was shown.
        var citations = GroundedPrompt.VerifyCitations(
            "Arsenal Football Club is an English club based in Islington, London. If you have "
            + "a specific context in mind, please provide more details! [1]\n\n"
            + "[1] This response is based on common associations.",
            UnrelatedSources,
            "arsenal");

        Assert.Empty(citations);
    }

    [Fact]
    public void A_citation_after_the_full_stop_is_judged_on_the_sentence_it_trails()
    {
        // The prompt allows a marker "before the full stop or after it", so a
        // trailing marker is an ordinary citation and must survive on the
        // strength of the sentence it follows rather than be waved through.
        var citations = GroundedPrompt.VerifyCitations(
            "Download the client from the IT portal and sign in with your network credentials. [1]",
            ThreeSources,
            "how do I connect to the VPN");

        Assert.Single(citations);
    }

    [Fact]
    public void One_word_in_common_is_coincidence_rather_than_support()
    {
        var citations = GroundedPrompt.VerifyCitations(
            "Some people add sugar or honey to balance the flavour of the juice [2].",
            [
                Passage("payments", "3. Relevant Data Model", "The ledger must balance."),
                Passage("payments", "3. Relevant Data Model", "The ledger must balance.", 1),
            ],
            "how to make orange juice");

        Assert.Empty(citations);
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
            "Download the client from the portal [see note] and sign in [1].", ThreeSources);

        Assert.Single(citations);
        Assert.Equal(1, citations[0].Marker);
    }
}
