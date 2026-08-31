using System.Text.RegularExpressions;
using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;

namespace DocHub.Services.Chat;

/// <summary>
/// What a follow-up question is actually searched for.
///
/// A question is retrieved on its own words, and a follow-up usually has none
/// worth retrieving on: "can you specify the paths?" names no subject, so the
/// nearest passages are whatever else in the library happens to talk about
/// paths. Measured against a real library, the passage holding the answer to
/// exactly that follow-up ranked 285th of 476 and fell outside the relevance
/// floor, while three unrelated passages sat comfortably inside it. The model
/// then declined — correctly, on what it had been handed — one turn after
/// quoting the very passage that answered the question.
///
/// The conversation was already being replayed to the model and never to
/// retrieval. This closes that gap: a question with no subject of its own is
/// searched together with the couple of questions before it, which is where
/// the subject actually is.
///
/// Pure functions, like <see cref="GroundedPrompt"/>, so the rule can be
/// reasoned about and tested without a database or a model.
/// </summary>
public static partial class ConversationQuery
{
    /// <summary>
    /// Below this many substantial words, a question is taken to be leaning on
    /// the conversation rather than standing on its own.
    ///
    /// Deliberately low. Anchoring a question that did not need it is how a
    /// change of subject gets searched for the previous subject, so the test
    /// has to fail towards leaving the question alone.
    /// </summary>
    private const int SelfContainedTerms = 3;

    /// <summary>
    /// How many earlier questions a thin one is allowed to carry.
    ///
    /// Two, because follow-ups come in runs and the subject is usually where
    /// the run started: "and the methods?" after "can you specify the paths?"
    /// needs the question before both of them. More than that and the oldest
    /// turn — likely a different subject — starts outweighing what was just
    /// asked.
    /// </summary>
    private const int AnchorQuestions = 2;

    /// <summary>
    /// Keeps a composed query near the length of an ordinary question. The
    /// embedding is of the whole string, so an anchor several times longer than
    /// the follow-up would answer the earlier question again.
    /// </summary>
    private const int MaxComposedLength = 400;

    /// <summary>
    /// The text to embed for this turn: the question alone when it carries its
    /// own subject, or the recent questions in front of it when it does not.
    /// </summary>
    /// <param name="session">
    /// The conversation so far, or null for the first question of a new one —
    /// where there is nothing to lean on and nothing to do.
    /// </param>
    /// <param name="question">The question as asked, already trimmed.</param>
    public static string For(ChatTranscriptDto? session, string question)
    {
        if (session is null || IsSelfContained(question)) return question;

        // Oldest first, so the composed text reads in the order it was asked.
        // Only the user's own words: an assistant turn is the model's, and
        // grounding the next search in it is how a wrong answer becomes the
        // subject of the following question.
        var anchors = session.Messages
            .Where(message => message.Role == ChatRole.User)
            .Select(message => message.Content.Trim())
            .Where(content => content.Length > 0)
            .TakeLast(AnchorQuestions)
            .ToList();

        if (anchors.Count == 0) return question;

        // The question is what was asked and is never dropped; the anchor is
        // context, so it is the oldest turn that gives way when the composed
        // text runs long.
        while (anchors.Count > 0
            && anchors.Sum(anchor => anchor.Length + 1) + question.Length > MaxComposedLength)
        {
            anchors.RemoveAt(0);
        }

        return anchors.Count == 0 ? question : string.Join(' ', [.. anchors, question]);
    }

    /// <summary>
    /// Whether a question has enough subject matter of its own to be searched
    /// as it stands.
    /// </summary>
    private static bool IsSelfContained(string question) =>
        Terms(question).Count >= SelfContainedTerms;

    /// <summary>
    /// Substantial words, counted the same way citation verification counts
    /// them: four characters and up, which is where the subject matter lives.
    /// One definition of "a word that means something", used by both.
    /// </summary>
    private static HashSet<string> Terms(string text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match word in WordPattern().Matches(text))
        {
            if (word.Length >= 4) terms.Add(word.Value.ToLowerInvariant());
        }

        return terms;
    }

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9_-]*")]
    private static partial Regex WordPattern();
}
