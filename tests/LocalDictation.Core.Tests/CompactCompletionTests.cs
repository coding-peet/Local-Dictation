using LocalDictation.Core;
using Xunit;

public class CompactCompletionTests
{
    [Theory]
    [InlineData(true,true,false,false,0,CompactCompletionAction.Insert)]
    [InlineData(true,true,true,true,0,CompactCompletionAction.Insert)]
    [InlineData(true,true,true,true,2,CompactCompletionAction.ReviewDifferences)]
    [InlineData(true,true,false,false,2,CompactCompletionAction.ReviewDifferences)]
    [InlineData(true,true,true,false,0,CompactCompletionAction.KeepPending)]
    [InlineData(false,true,true,true,0,CompactCompletionAction.KeepPending)]
    [InlineData(false,true,false,false,0,CompactCompletionAction.KeepPending)]
    [InlineData(true,false,false,false,0,CompactCompletionAction.KeepPending)]
    [InlineData(false,false,true,false,2,CompactCompletionAction.KeepPending)]
    public void AutomaticPlacementRequiresCompletedPrimaryAndRequestedVerification(
        bool normal,bool final,bool requested,bool verified,int unresolved,CompactCompletionAction expected)
    {
        Assert.Equal(expected,CompactCompletion.Decide(normal,final,requested,verified,unresolved));
    }

    [Theory]
    [InlineData(false,false,0,CompactCompletionAction.Preview)]
    [InlineData(true,true,0,CompactCompletionAction.Preview)]
    [InlineData(true,true,2,CompactCompletionAction.Preview)]
    [InlineData(true,false,0,CompactCompletionAction.KeepPending)]
    public void PreviewNeverAutomaticallyInserts(bool verify,bool completed,int unresolved,CompactCompletionAction expected)
    {
        Assert.Equal(expected,CompactCompletion.Decide(true,true,verify,completed,unresolved,AfterRecordingAction.PreviewFirst));
    }

    [Fact]
    public void UnknownOutputModeFailsClosed()=>Assert.Equal(CompactCompletionAction.KeepPending,
        CompactCompletion.Decide(true,true,false,false,0,(AfterRecordingAction)99));

    [Fact]
    public void PreviewPreservesLivePreferenceAndOldSettingsKeepDirectInsertion()
    {
        var settings=System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"InsertAtCursor\":true}")!;
        Assert.Equal(AfterRecordingAction.InsertDirectly,settings.AfterRecording);
        Assert.True(settings.CanInsertLive);
        settings.AfterRecording=AfterRecordingAction.PreviewFirst;
        Assert.False(settings.CanInsertLive);
        Assert.True(settings.InsertAtCursor);
        var copy=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.Equal(AfterRecordingAction.PreviewFirst,copy.AfterRecording);
        copy.AfterRecording=AfterRecordingAction.InsertDirectly;
        Assert.True(copy.CanInsertLive);
    }
}
