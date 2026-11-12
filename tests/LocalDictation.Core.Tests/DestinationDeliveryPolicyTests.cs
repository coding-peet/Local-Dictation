using LocalDictation.Core;
using Xunit;

public class DestinationDeliveryPolicyTests
{
    [Theory]
    [InlineData("olk")]
    [InlineData("OLK")]
    [InlineData("oLk")]
    [InlineData("OUTLOOK")]
    [InlineData("outlook")]
    [InlineData("OutLook")]
    public void OutlookProcessesKeepDraftUpdatesOutOfTheDestination(string processName)
    {
        Assert.True(DestinationDeliveryPolicy.InsertsAfterFinish(processName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("notepad")]
    [InlineData("chrome")]
    [InlineData("olk.exe")]
    [InlineData("OUTLOOK.EXE")]
    [InlineData("C:\\Program Files\\Microsoft Office\\OUTLOOK.EXE")]
    [InlineData("olk-preview")]
    [InlineData("my-outlook-window")]
    [InlineData("Inbox - Outlook")]
    [InlineData(" outlook")]
    [InlineData("outlook ")]
    public void OtherNamesKeepTheExistingLiveDeliveryPolicy(string? processName)
    {
        Assert.False(DestinationDeliveryPolicy.InsertsAfterFinish(processName));
    }
}
