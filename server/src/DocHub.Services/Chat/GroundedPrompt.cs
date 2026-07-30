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
        IReadOnlyList<RetrievedPassage> passages)
    {
        var cited = new List<Citation>();
        var seen = new HashSet<int>();

        foreach (Match match in CitationMarker().Matches(answer))
        {
            if (!int.TryParse(match.Groups[1].Value, out var marker)) continue;

            // Markers are 1-based in the prompt so they read naturally.
            var index = marker - 1;
            if (index < 0 || index >= passages.Count) continue;
            if (!seen.Add(marker)) continue;

            var passage = passages[index];

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
