using LocalDictation.Core;
using Xunit;

public sealed class WindowsStartupLaunchPolicyTests
{
    [Theory]
    [InlineData()]
    [InlineData("--no-hotkey")]
    [InlineData("--startup=true")]
    public void ManualLaunchShowsSettingsAndExistingInstanceMessage(params string[] args)
    {
        var policy = WindowsStartupLaunchPolicy.FromArguments(args);
        Assert.True(policy.ShowSettings);
        Assert.True(policy.ShowAlreadyRunningMessage);
    }

    [Theory]
    [InlineData("--startup")]
    [InlineData("--no-hotkey", "--startup")]
    [InlineData("--STARTUP")]
    public void StartupLaunchKeepsTrayAndDuplicateInstanceSilent(params string[] args)
    {
        var policy = WindowsStartupLaunchPolicy.FromArguments(args);
        Assert.False(policy.ShowSettings);
        Assert.False(policy.ShowAlreadyRunningMessage);
    }
}
