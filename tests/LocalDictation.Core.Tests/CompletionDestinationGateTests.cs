using LocalDictation.Core;
using Xunit;

public class CompletionDestinationGateTests
{
    [Fact]
    public void ReturningDuringRecordingAllowsNormalAutomaticCompletion()
    {
        var gate = new CompletionDestinationGate();
        gate.Interrupt(); // Focus left and returned while recording.
        gate.BeginFinish();
        Assert.False(gate.CanInsertAutomatically); // Native field check is still pending.
        gate.ConfirmOriginalField(true);
        Assert.True(gate.CanInsertAutomatically);
    }

    [Fact]
    public void FinishingElsewhereNeverBecomesAutomaticOnLaterReturn()
    {
        var gate = new CompletionDestinationGate();
        gate.BeginFinish();
        gate.ConfirmOriginalField(false);
        gate.BeginFinish();
        gate.ConfirmOriginalField(true);
        Assert.True(gate.IsFinishing);
        Assert.False(gate.CanInsertAutomatically);
    }

    [Fact]
    public void InputWhileCheckingTheOriginalFieldCannotBeOverruledByLateReadback()
    {
        var gate = new CompletionDestinationGate();
        gate.BeginFinish();
        gate.Interrupt();
        gate.ConfirmOriginalField(true);
        Assert.False(gate.CanInsertAutomatically);
    }

    [Fact]
    public void LeavingAfterFinishAlsoRequiresExplicitInsertion()
    {
        var gate = new CompletionDestinationGate();
        gate.BeginFinish();
        gate.ConfirmOriginalField(true);
        gate.Interrupt();
        gate.ConfirmOriginalField(true);
        Assert.False(gate.CanInsertAutomatically);
    }
}
