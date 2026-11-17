using LocalDictation.Core;
using Xunit;

public class DeliveryCompatibilityTests
{
    [Fact]
    public void TerminalFailureChangesLaterSessionsWithoutChangingTheLivePreference()
    {
        var settings = new Settings { Live = true, InsertAtCursor = true };
        Assert.False(DestinationDeliveryPolicy.InsertsAfterFinish("Codex", settings.FinalOnlyApplications));
        Assert.True(DestinationDeliveryPolicy.RememberLiveFailure(settings.FinalOnlyApplications, "Codex", true));
        Assert.True(DestinationDeliveryPolicy.InsertsAfterFinish("CODEX", settings.FinalOnlyApplications));
        Assert.False(DestinationDeliveryPolicy.InsertsAfterFinish("notepad", settings.FinalOnlyApplications));
        Assert.True(settings.Live);
        Assert.True(settings.InsertAtCursor);
        Assert.False(DestinationDeliveryPolicy.RememberLiveFailure(settings.FinalOnlyApplications, "codex", true));
        Assert.Single(settings.FinalOnlyApplications);
    }

    [Fact]
    public void FocusPausesAndUserEditsDoNotDisableLiveForTheApplication()
    {
        var apps = new List<string>();
        Assert.False(DestinationDeliveryPolicy.RememberLiveFailure(apps, "chrome", false));
        Assert.Empty(apps);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("C:\\Apps\\editor.exe")]
    public void UnidentifiedDestinationsAreNotRemembered(string? process)
    {
        var apps = new List<string>();
        Assert.False(DestinationDeliveryPolicy.RememberLiveFailure(apps, process, true));
        Assert.Empty(apps);
    }

    [Fact]
    public void CompatibilityListSurvivesSavingAndCanBeResetWithoutChangingOutlook()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LocalDictation-policy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new Settings();
            DestinationDeliveryPolicy.RememberLiveFailure(settings.FinalOnlyApplications, "chrome", true);
            settings.Save(directory);
            var loaded = Settings.Load(directory);
            Assert.True(DestinationDeliveryPolicy.InsertsAfterFinish("CHROME", loaded.FinalOnlyApplications));
            loaded.FinalOnlyApplications.Clear();
            loaded.Save(directory);
            var reset = Settings.Load(directory);
            Assert.False(DestinationDeliveryPolicy.InsertsAfterFinish("chrome", reset.FinalOnlyApplications));
            Assert.True(DestinationDeliveryPolicy.InsertsAfterFinish("OUTLOOK", reset.FinalOnlyApplications));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
