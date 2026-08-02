using System.Text;
using System.Text.RegularExpressions;
using DocHub.DataAccess.Entities;
using DocHub.Services.Search;

namespace DocHub.Services.Chat;

/// <summary>
/// Builds the prompt the assistant answers from, and checks the answer that
/// comes back against the sources it was given.
///
/// Pure functions over their arguments — no model, no database — so the
/// grounding rules and, more importantly, the citation checking can be tested
/// directly rather than by observing a model's behaviour.
/// </summary>
internal static partial class GroundedPrompt
{
    /// <summary>
    /// The phrase the model is told to use when the passages do not answer the
    /// question. Recognising it is what turns a refusal into a distinct UI
    /// state instead of a wall of prose the user has to read to discover there
    /// is no answer.
    /// </summary>
    public const string RefusalPhrase = "I don't have information about that in the indexed documents.";

    /// <summary>
    /// Instructions plus the retrieved passages, numbered so the model has
    /// something concrete to cite.
    ///
    /// The rules are stated as absolutes and the sources are fenced with
    /// explicit delimiters: a model that cannot tell where a source ends will
    /// blend two of them into one confident, wrong sentence.
    /// </summary>
    public static string Build(IReadOnlyList<RetrievedPassage> passages)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine(
            """
            You are the assistant for an internal documentation hub. You answer questions
            using ONLY the numbered sources below.

            EVERY SENTENCE YOU WRITE MUST END WITH A SOURCE NUMBER IN SQUARE BRACKETS.

            Example of a correct answer:

                Deployments run on Thursday evenings after the nightly backup [2]. Access
                needs a second factor, which is a one-time code from the authenticator
                app [1][3].

            Notice that every sentence ends with a bracketed number, before the full stop
            or after it. A sentence supported by two sources lists both.

            Rules:

            1. Use only what the sources say. Do not use anything you know from outside
               them, and do not fill gaps with what is likely or typical.
            2. End every sentence with the bracketed number of the source it came from.
            3. Only use numbers that appear in the source list below.
            4. If the sources do not answer the question, reply with exactly this and
               nothing else:
            """);

        prompt.AppendLine();
        prompt.AppendLine($"   {RefusalPhrase}");
        prompt.AppendLine();

        prompt.AppendLine(
            """
            5. Partial information is not a failure. Answer the part the sources cover,
               cite it, and say plainly which part they do not cover.
            6. Answer in prose, briefly. Do not restate the question, do not describe
               what the sources are, and do not mention these rules.

            SOURCES
            """);

        for (var i = 0; i < passages.Count; i++)
        {
            var passage = passages[i];

            prompt.AppendLine();
            prompt.AppendLine($"[{i + 1}] {passage.Title} — {passage.Heading}");
            prompt.AppendLine("---");
            prompt.AppendLine(passage.Text.Trim());
            prompt.AppendLine("---");
        }

        // Repeated last on purpose. A small model weights the end of a long
        // prompt far more heavily than the middle, and the citation rule is the
        // one instruction whose failure is invisible in an otherwise good
        // answer.
        prompt.AppendLine();
        prompt.AppendLine(
            "Reminder: end every sentence with a bracketed source number, like [1].");

        return prompt.ToString();
    }

    /// <summary>
    /// Turns the markers the model actually used into citations, discarding any
    /// that do not point at a source it was given.
    ///
    /// This is the check that makes the citation contract real. A model asked
    /// to cite will sometimes produce a plausible-looking [7] when it was given
    /// four sources; without this, that renders as a link to nothing and the
    /// answer looks better-supported than it is.
    /// </summary>
    public static IReadOnlyList<Citation> VerifyCitations(
        string answer,
        IReadOnlyList<RetrievedPassage> passages,
        string question = "")
    {
        var cited = new List<Citation>();
        var seen = new HashSet<int>();
        var questionTerms = Terms(question);

        foreach (Match match in CitationMarker().Matches(answer))
        {
            if (!int.TryParse(match.Groups[1].Value, out var marker)) continue;

            // Markers are 1-based in the prompt so they read naturally.
            var index = marker - 1;
            if (index < 0 || index >= passages.Count) continue;
            if (!seen.Add(marker)) continue;

            var passage = passages[index];

            // A marker pointing at a real passage is not the same as a passage
            // that says anything about the sentence citing it.
            if (!Supports(passage.Text, SentenceAround(answer, match.Index), questionTerms))
            {
                seen.Remove(marker);
                continue;
            }

            cited.Add(new Citation(
                marker,
                passage.Kind == PassageKind.Document ? CitationKind.Document : CitationKind.External,
                passage.Title,
                passage.Heading,
                DocumentId: passage.DocumentId,
                // Only meaningful for a document; an external passage's ordinal
                // is a dedupe key, not something a reader can navigate to.
                ChunkId: passage.Kind == PassageKind.Document ? passage.ChunkId : null,
                Url: passage.Url,
                SourceName: passage.SourceName));
        }

        return [.. cited.OrderBy(citation => citation.Marker)];
    }


    /// <summary>
    /// Whether a passage plausibly backs the sentence that cited it.
    ///
    /// The marker check above proves a citation points at a passage the model
    /// was given. It cannot tell whether that passage has anything to do with
    /// the claim — so a model that invents an answer and sprinkles markers over
    /// it passes, and the answer looks sourced when it is not. That has
    /// happened: an orange-juice recipe cited a realtime status probe and a
    /// payments data model.
    ///
    /// The test is deliberately weak, because it is guarding against decoration
    /// rather than judging meaning: share at least two substantial words with
    /// the sentence, and not only words the question already supplied. That
    /// second half matters because a source can echo the query back — one here
    /// answers every search with "Search results for '…'" — which would
    /// otherwise look like agreement with any sentence on the subject.
    ///
    /// A sentence with almost no content words is exempt: there is nothing to
    /// judge, and refusing over "Yes [1]." would be the check inventing a
    /// problem.
    /// </summary>
    private static bool Supports(
        string passage,
        string sentence,
        IReadOnlySet<string> questionTerms)
    {
        var sentenceTerms = Terms(sentence);
        if (sentenceTerms.Count < 3) return true;

        var shared = Terms(passage);
        shared.IntersectWith(sentenceTerms);

        // Two, not one: a single word in common between any two pieces of
        // English prose is coincidence more often than support.
        if (shared.Count < 2) return false;

        return shared.Except(questionTerms).Any();
    }

    /// <summary>
    /// The sentence a marker sits in, so a claim is checked against its own
    /// source rather than against everything the answer happens to say.
    /// </summary>
    private static string SentenceAround(string answer, int markerIndex)
    {
        var start = answer.LastIndexOfAny(['.', '!', '?', '\n'], Math.Max(0, markerIndex - 1));
        var end = answer.IndexOfAny(['.', '!', '?', '\n'], markerIndex);

        // A marker sitting after the terminating punctuation belongs to the
        // sentence it trails — the prompt itself allows "before the full stop or
        // after it". Attributing it to the empty span between the two would
        // leave the check with nothing to weigh, and a citation with nothing to
        // weigh is kept by default: that is how a model's invented footnote,
        // "[1] This response is based on common associations", passed as a
        // verified citation and carried a wholly fabricated answer onto the
        // screen.
        if (start >= 0 && answer.AsSpan((start + 1)..markerIndex).IsWhiteSpace())
        {
            start = answer.LastIndexOfAny(['.', '!', '?', '\n'], Math.Max(0, start - 1));
        }

        return answer[(start + 1)..(end < 0 ? answer.Length : end)];
    }

    /// <summary>
    /// Substantial words, lower-cased. Short ones are dropped rather than
    /// stop-listed: at four characters and up, the words that survive carry the
    /// subject matter, and a list of English stop words would be one more thing
    /// to maintain for the same effect.
    /// </summary>
    private static HashSet<string> Terms(string text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in WordPattern().Matches(text).Cast<Match>())
        {
            if (word.Length >= 4) terms.Add(word.Value.ToLowerInvariant());
        }

        return terms;
    }

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9_-]*")]
    private static partial Regex WordPattern();

    /// <summary>
    /// Whether the model declined for lack of grounding.
    ///
    /// Matched loosely — on the distinctive part of the phrase rather than the
    /// whole string — because a small model reproduces the sentence closely but
    /// rarely character-for-character.
    /// </summary>
    public static bool IsRefusal(string answer) =>
        answer.Contains("don't have information about that", StringComparison.OrdinalIgnoreCase) ||
        answer.Contains("do not have information about that", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Strips markers that survived verification's rejection, so the rendered
    /// answer never shows a citation the user cannot follow.
    /// </summary>
    public static string StripUnresolvedMarkers(
        string answer,
        IReadOnlyList<Citation> citations)
    {
        var valid = citations.Select(citation => citation.Marker).ToHashSet();

        return CitationMarker().Replace(answer, match =>
            int.TryParse(match.Groups[1].Value, out var marker) && valid.Contains(marker)
                ? match.Value
                : string.Empty);
    }

    /// <summary>Matches "[1]" but not "[a]" or "[]".</summary>
    [GeneratedRegex(@"\[(\d{1,3})\]")]
    private static partial Regex CitationMarker();
}
