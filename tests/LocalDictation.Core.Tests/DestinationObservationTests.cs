using LocalDictation.Core;
using Xunit;

public class DestinationObservationTests
{
    [Fact] public void RecordingControlsKeepTheOriginalCursorOwned()
    {
        var epoch=new DestinationEpoch();
        var original=epoch.Capture(10);
        Assert.False(epoch.UserInput(20,20));
        Assert.True(epoch.CanAdopt(original,10));
    }

    [Fact] public void AnOutsideClickCannotBeUndoneByReturningToTheBanner()
    {
        var epoch=new DestinationEpoch();
        var original=epoch.Capture(10);
        Assert.True(epoch.UserInput(30,20));
        Assert.False(epoch.UserInput(20,20));
        Assert.False(epoch.CanAdopt(original,10));
    }

    [Fact] public void UnknownWindowHandlesAndKeyboardInputStillInvalidate()
    {
        var epoch=new DestinationEpoch();
        var original=epoch.Capture(10);
        Assert.True(epoch.UserInput(0,0));
        Assert.False(epoch.CanAdopt(original,10));
        var next=epoch.Capture(10);
        epoch.UserInput();
        Assert.False(epoch.CanAdopt(next,10));
    }
}
