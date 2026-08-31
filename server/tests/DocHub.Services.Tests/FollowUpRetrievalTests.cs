using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Embeddings;
using DocHub.Services.Chat;
using DocHub.Services.Knowledge;
using DocHub.Services.Search;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Tests;

/// <summary>
/// What a follow-up question is searched for.
///
/// The bug these are written against: an answer quoted a passage in one turn,
/// and the next turn — "can you specify the paths?" — was searched on those
/// five words alone. The passage holding the answer ranked 285th of 476 and
/// fell outside the relevance floor, three unrelated passages did not, and the
/// assistant declined. The conversation was replayed to the model and never to
/// retrieval.
/// </summary>
public sealed class FollowUpRetrievalTests
{
    [Fact]
    public void A_question_that_names_its_own_subject_is_searched_as_asked()
    {
        var session = Transcript("how do I rotate a personal access token?");

        // Nothing is prepended: it has a subject, and dragging the previous one
        // in would search for a question nobody asked.
        Assert.Equal(
            "where are the audit logs kept?",
            ConversationQuery.For(session, "where are the audit logs kept?"));
    }

    [Fact]
    public void A_follow_up_with_no_subject_is_anchored_to_the_question_before_it()
    {
        var session = Transcript("how to get the Activity Analytics?");

        Assert.Equal(
            "how to get the Activity Analytics? can you specify the paths?",
            ConversationQuery.For(session, "can you specify the paths?"));
    }

    [Fact]
    public void The_first_question_of_a_conversation_has_nothing_to_lean_on()
    {
        // Null session, not an empty one: this is the very first turn, and the
        // question is searched exactly as typed however thin it is.
        Assert.Equal("what about the paths?", ConversationQuery.For(null, "what about the paths?"));
    }

    [Fact]
    public void A_run_of_follow_ups_keeps_the_thread_it_hangs_from()
    {
        // Carrying only the turn immediately before would anchor to another
        // question that says nothing either, and the subject would be lost one
        // turn at a time.
        var session = Transcript(
            "how to get the Activity Analytics?",
            "can you specify the paths?");

        Assert.Equal(
            "how to get the Activity Analytics? can you specify the paths? and the methods?",
            ConversationQuery.For(session, "and the methods?"));
    }

    [Fact]
    public void An_answer_is_never_part_of_the_search()
    {
        var session = new ChatTranscriptDto(
            Session(2),
            [
                Message(ChatRole.User, "hello"),
                // Grounding the next search in the model's own words is how a
                // wrong answer becomes the subject of the following question.
                Message(ChatRole.Assistant, "Deployments run after the nightly backup window."),
            ]);

        var composed = ConversationQuery.For(session, "what about it?");

        Assert.DoesNotContain("Deployments", composed);
        Assert.EndsWith("what about it?", composed);
    }

    [Fact]
    public async Task The_anchored_question_is_what_the_vector_branch_embeds()
    {
        var embeddings = new RecordingEmbeddingProvider();

        var search = new SearchService(
            new EmptyChunkRepository(),
            embeddings,
            Options.Create(new KnowledgeOptions()),
            NullLogger<SearchService>.Instance);

        await search.RetrieveAsync(new SearchRequest
        {
            Query = "can you specify the paths?",
            SemanticQuery = "how to get the Activity Analytics? can you specify the paths?",
        });

        Assert.Equal(
            "how to get the Activity Analytics? can you specify the paths?",
            embeddings.LastQuery);
    }

    [Fact]
    public async Task The_keyword_branch_still_searches_for_what_was_typed()
    {
        var chunks = new EmptyChunkRepository();

        var search = new SearchService(
            chunks,
            new RecordingEmbeddingProvider(),
            Options.Create(new KnowledgeOptions()),
            NullLogger<SearchService>.Instance);

        await search.RetrieveAsync(new SearchRequest
        {
            Query = "can you specify the paths?",
            SemanticQuery = "how to get the Activity Analytics? can you specify the paths?",
        });

        // The keyword query is ANDed, so every word the anchor adds is one more
        // term a chunk would have to contain. Widening it there would match
        // less, not more.
        Assert.Equal("can you specify the paths?", chunks.LastKeywordText);
    }

    private static ChatTranscriptDto Transcript(params string[] questions) =>
        new(
            Session(questions.Length),
            [.. questions.Select(question => Message(ChatRole.User, question))]);

    private static ChatSessionDto Session(int messages) =>
        new(Guid.CreateVersion7(), "Session", messages, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static ChatMessageDto Message(ChatRole role, string content) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), role, content, [], false,
            DateTimeOffset.UtcNow, [], []);

    /// <summary>
    /// No chunks, and a record of what each branch was asked for. The interest
    /// here is which text reaches which branch, not what comes back — that is
    /// covered against real Postgres in <see cref="HybridSearchTests"/>.
    /// </summary>
    private sealed class EmptyChunkRepository : IChunkRepository
    {
        public string? LastKeywordText { get; private set; }

        public Task<IReadOnlyList<ChunkMatchDto>> SearchKeywordAsync(
            ChunkSearchDto query,
            CancellationToken ct = default)
        {
            LastKeywordText = query.Text;
            return Task.FromResult<IReadOnlyList<ChunkMatchDto>>([]);
        }

        public Task<IReadOnlyList<ChunkMatchDto>> SearchVectorAsync(
            ChunkSearchDto query,
            float[] queryEmbedding,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChunkMatchDto>>([]);

        public Task<IReadOnlyList<ChunkMatchDto>> GetForDocumentAsync(
            Guid documentId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChunkMatchDto>>([]);

        public Task ReplaceAsync(
            Guid documentId,
            string sourceBlobSha,
            IReadOnlyList<NewChunkDto> chunks,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> DeleteForDocumentAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class RecordingEmbeddingProvider : IEmbeddingProvider
    {
        public string Name => "recording";

        public int Dimensions => 8;

        public string? LastQuery { get; private set; }

        public Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default)
        {
            LastQuery = text;
            return Task.FromResult(new float[Dimensions]);
        }

        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(_ => new float[Dimensions])]);

        public Task<EmbeddingAvailability> CheckAvailabilityAsync(CancellationToken ct = default) =>
            Task.FromResult(new EmbeddingAvailability(true, "Recording."));
    }
}
