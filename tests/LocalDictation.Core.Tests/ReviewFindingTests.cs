using LocalDictation.Core;
using Xunit;

public class ReviewFindingTests
{
    [Fact] public void ChoiceUndoPreservesLaterManualDraft()
    {
        var d=new ReviewDocument{Pieces=[new("x","a","b",true)]};
        d.Choose("x","verification");
        d.ManualFinalDraft="Later manual wording";
        d.Undo();
        Assert.Equal("Later manual wording",d.OutputText);
    }

    [Fact] public void RebuildKeepsEarlierDraftVersions()
    {
        var d=new ReviewDocument{ManualFinalDraft="First"};
        d.RebuildDraft();d.RebuildDraft();
        Assert.Equal("First",d.PreviousManualDraft);
        d.ManualFinalDraft="Second";d.RebuildDraft();
        Assert.Contains("First",d.ManualDraftArchive);
        Assert.Contains("Second",d.ManualDraftArchive);
    }

    [Theory]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0x41)]
    public void HeldThirdKeyCannotTriggerModifierChord(int other)
    {
        var c=new ModifierChord();
        c.Update(other,true);c.Update(0x42,true);c.Update(0x42,false);
        c.Update(0xA2,true);c.Update(0xA0,true);
        Assert.False(c.Update(0xA0,false));
    }

    [Fact] public void MultipleShiftKeysDoNotToggleUntilChordEnds()
    {
        var c=new ModifierChord();
        c.Update(0xA2,true);c.Update(0xA0,true);c.Update(0xA1,true);
        Assert.False(c.Update(0xA0,false));
        Assert.True(c.Update(0xA1,false));
        Assert.False(c.Update(0xA2,false));
    }

    [Fact] public void EscapeCancelsOnlyAnActiveSession()
    {
        var input=new InputCommands();
        Assert.Equal(InputCommand.None,input.Update(0x1B,true,false,true).Command);
        input.Update(0x1B,false,false,true);
        var cancel=input.Update(0x1B,true,true,true);
        Assert.Equal(InputCommand.Cancel,cancel.Command);Assert.True(cancel.Suppress);
        Assert.True(input.Update(0x1B,false,false,true).Suppress);
    }

    [Fact] public void StartupCaptureCannotAdoptAfterInputOrWindowChange()
    {
        var epoch=new DestinationEpoch();
        var lease=epoch.Capture(123);
        epoch.UserInput();
        Assert.False(epoch.CanAdopt(lease,123));
        Assert.False(epoch.CanAdopt(epoch.Capture(123),456));
        Assert.True(epoch.CanAdopt(epoch.Capture(123),123));
    }

    [Fact] public void RecordingBudgetStopsNormallyAtAcceptedLimit()
    {
        var budget=new AudioBudget(100);
        Assert.Equal(40,budget.Accept(40));
        Assert.Equal(60,budget.Accept(80));
        Assert.True(budget.Reached);
        Assert.Equal(0,budget.Accept(2));
    }

    [Fact] public void HeartbeatsDoNotExtendAbsoluteFinalizationDeadline()
    {
        var clock=TimeSpan.Zero;
        var deadline=new SessionDeadline(()=>clock,TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(4),TimeSpan.FromSeconds(5));
        clock=TimeSpan.FromSeconds(1);deadline.Observe("session.started");
        deadline.Finish();
        clock=TimeSpan.FromSeconds(4);deadline.Observe("session.progress");
        clock=TimeSpan.FromSeconds(6);
        Assert.Throws<TimeoutException>(()=>{deadline.Remaining();});
    }

    [Fact] public void EnglishOnlyVerifierEligibilityRespectsEffectiveLanguage()
    {
        var en=new ModelInfo{Installed=true,RuntimeTested=true,Languages=["en"]};
        var sk=new ModelInfo{Installed=true,RuntimeTested=true,Languages=["sk"]};
        Assert.False(ModelEligibility.CanVerify(sk,"en",en));
        Assert.True(ModelEligibility.CanVerify(en,"auto",en));
    }
    [Fact] public void HeartbeatCannotReplaceStartupAcknowledgement()
    {
        var clock=TimeSpan.Zero;
        var deadline=new SessionDeadline(()=>clock,TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(4),TimeSpan.FromSeconds(5));
        clock=TimeSpan.FromSeconds(1);deadline.Observe("session.progress");
        clock=TimeSpan.FromSeconds(3);
        Assert.Throws<TimeoutException>(()=>{deadline.Remaining();});
    }

}
