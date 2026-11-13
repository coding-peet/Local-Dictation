using LocalDictation.Core;
using Xunit;

public class FinishQueueAndProjectionTests
{
    [Fact]
    public async Task LiveWorkQueuedBeforeFinishMustBeRevokedBeforeItStarts()
    {
        var gate = new CompletionDestinationGate();
        Assert.True(gate.CanBeginLiveDelivery);
        var releaseWorker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = Task.Run(async () => { await releaseWorker.Task; return gate.CanBeginLiveDelivery; });
        gate.BeginFinish();
        gate.ConfirmOriginalField(false);
        releaseWorker.SetResult();
        Assert.False(await queued);
    }

    [Fact]
    public void CapturedEmptyValueCannotHideAnIdenticalOwnedPayloadDuringWithdrawal()
    {
        const string projection = "Write a message";
        var guard = new TargetGuard("editor", "", 0);
        Assert.True(guard.CanUseInitialEmptyProjection(projection, projection, 0));
        guard.Applied(projection, projection, projection.Length);
        // Delete was rejected: text is retained but the provider now exposes caret0/empty Value.
        Assert.False(guard.CanUseInitialEmptyProjection(projection, projection, 0));
        Assert.Equal(projection, guard.OwnedText);
        Assert.False(guard.CanReplace("editor", "", 0, 0));
    }

    [Fact]
    public void ACompletedWithdrawalDoesNotReenableTheInitialProjectionHeuristic()
    {
        var guard = new TargetGuard("editor", "", 0);
        guard.Applied("draft", "draft", 5);
        guard.Applied("", "", 0);
        Assert.False(guard.CanUseInitialEmptyProjection("placeholder", "placeholder", 0));
    }
}
