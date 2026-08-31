using DocHub.Services.Repository;

namespace DocHub.Services.Tests;

/// <summary>
/// What a delivery from GitLab is allowed to cause.
///
/// This endpoint is anonymous of necessity — GitLab holds no session — and it
/// is the only anonymous endpoint that causes work, so the rules about what it
/// accepts are the security boundary rather than a convenience.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class WebhookTests(StackFixture fixture)
{
    [Fact]
    public async Task A_push_to_the_mirrored_branch_queues_a_sync()
    {
        await using var scope = fixture.NewScope();

        var outcome = await scope.Webhook.HandleAsync(
            "test-secret", "Push Hook", "refs/heads/main");

        Assert.Equal(WebhookOutcome.Queued, outcome);

        // Queued rather than run inline: GitLab hangs up on a webhook that does
        // not answer promptly, and a full mirror takes minutes.
        Assert.Single(scope.SyncQueue.Queued);
        Assert.Null(scope.SyncQueue.Queued[0]);
    }

    [Fact]
    public async Task A_wrong_token_is_refused_and_queues_nothing()
    {
        await using var scope = fixture.NewScope();

        var outcome = await scope.Webhook.HandleAsync(
            "not-the-secret", "Push Hook", "refs/heads/main");

        Assert.Equal(WebhookOutcome.Rejected, outcome);
        Assert.Empty(scope.SyncQueue.Queued);
    }

    [Fact]
    public async Task A_missing_token_is_refused()
    {
        await using var scope = fixture.NewScope();

        Assert.Equal(
            WebhookOutcome.Rejected,
            await scope.Webhook.HandleAsync(null, "Push Hook", "refs/heads/main"));
        Assert.Empty(scope.SyncQueue.Queued);
    }

    [Fact]
    public async Task A_push_to_another_branch_is_ignored()
    {
        await using var scope = fixture.NewScope();

        var outcome = await scope.Webhook.HandleAsync(
            "test-secret", "Push Hook", "refs/heads/feature/rewrite");

        // Not an error — GitLab disables a hook that keeps failing, and a push
        // to a branch the hub does not mirror is a perfectly ordinary event.
        // Syncing on one would re-list the whole tree for every branch a busy
        // team pushes to.
        Assert.Equal(WebhookOutcome.Ignored, outcome);
        Assert.Empty(scope.SyncQueue.Queued);
    }

    [Fact]
    public async Task An_event_that_is_not_a_push_is_ignored()
    {
        await using var scope = fixture.NewScope();

        var outcome = await scope.Webhook.HandleAsync(
            "test-secret", "Pipeline Hook", "refs/heads/main");

        Assert.Equal(WebhookOutcome.Ignored, outcome);
        Assert.Empty(scope.SyncQueue.Queued);
    }
}
