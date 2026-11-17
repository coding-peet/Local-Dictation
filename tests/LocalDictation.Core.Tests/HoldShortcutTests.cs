using LocalDictation.Core;
using System.Text.Json;
using Xunit;

public class HoldShortcutTests
{
    [Fact]
    public void PublicContractPreservesToggleAsTheDefault()
    {
        var mode = typeof(Settings).Assembly.GetType("LocalDictation.Core.ShortcutMode");
        Assert.NotNull(mode);
        Assert.Equal(new[] { "Toggle", "Hold" }, Enum.GetNames(mode));
        Assert.Equal("Toggle", typeof(Settings).GetProperty("ShortcutMode")?.GetValue(new Settings())?.ToString());
        Assert.Equal("Toggle", typeof(InputCommands).GetProperty("Mode")?.GetValue(new InputCommands())?.ToString());
        Assert.Equal(false, typeof(InputCommands).GetProperty("IsHoldingShortcut")?.GetValue(new InputCommands()));
        Assert.Equal(0, (int)InputCommand.None);
        Assert.Equal(1, (int)InputCommand.Toggle);
        Assert.Equal(2, (int)InputCommand.Cancel);
        Assert.Contains("Start", Enum.GetNames<InputCommand>());
        Assert.Contains("Stop", Enum.GetNames<InputCommand>());
    }

    [Fact]
    public void SettingsPersistTheShortcutModeAsAStringAndKeepTheLegacyDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "ld-hold-settings-" + Guid.NewGuid());
        try
        {
            new Settings { ShortcutMode = ShortcutMode.Hold }.Save(path);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "settings.json")));
            Assert.Equal("Hold", json.RootElement.GetProperty("ShortcutMode").GetString());
            Assert.Equal(ShortcutMode.Hold, Settings.Load(path).ShortcutMode);
            File.WriteAllText(Path.Combine(path, "settings.json"), "{}");
            Assert.Equal(ShortcutMode.Toggle, Settings.Load(path).ShortcutMode);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void InvalidShortcutModeCannotOverwriteSavedSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "ld-hold-invalid-" + Guid.NewGuid());
        try
        {
            new Settings().Save(path);
            var original = File.ReadAllBytes(Path.Combine(path, "settings.json"));
            var invalid = new Settings { ShortcutMode = (ShortcutMode)1234 };
            Assert.Throws<ArgumentException>(() => invalid.Save(path));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(path, "settings.json")));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void ModifierHoldStartsOnTheCompleteChordAndStopsOnTheFirstRelease()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        Assert.Equal(new(InputCommand.None, false), input.Update(0x11, true, false, true));
        Assert.Equal(new(InputCommand.Start, false), input.Update(0x10, true, false, true));
        Assert.True(input.IsHoldingShortcut);
        Assert.Equal(new(InputCommand.None, false), input.Update(0x10, true, true, true));
        Assert.Equal(new(InputCommand.Stop, false), input.Update(0x11, false, true, true));
        Assert.False(input.IsHoldingShortcut);
        Assert.Equal(new(InputCommand.None, false), input.Update(0x10, false, true, true));
        Assert.Equal(new(InputCommand.None, false), input.Update(0x11, false, true, true));
    }

    [Fact]
    public void CustomHoldSuppressesOnlyItsNonmodifierAndStopsBeforeSessionActivation()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, Shortcut = ShortcutSpec.Parse("Ctrl+Alt+Space") };
        Assert.False(input.Update(0xA3, true, false, true).Suppress);
        Assert.False(input.Update(0xA5, true, false, true).Suppress);
        Assert.Equal(new(InputCommand.Start, true), input.Update(0x20, true, false, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, true, false, true));
        Assert.Equal(new(InputCommand.Stop, false), input.Update(0xA3, false, false, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, true, false, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, false, false, true));
        Assert.False(input.Update(0xA5, false, false, true).Suppress);
    }

    [Theory]
    [InlineData(0x11, 0x10, 0x11)]
    [InlineData(0x10, 0x11, 0x10)]
    [InlineData(0xA2, 0xA0, 0xA0)]
    [InlineData(0xA3, 0xA1, 0xA3)]
    [InlineData(0xA0, 0xA3, 0xA3)]
    [InlineData(0xA1, 0xA2, 0xA1)]
    public void ModifierVariantsAndReleaseOrderProduceOneStartAndStop(int first, int second, int released)
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        Assert.Equal(new(InputCommand.None, false), input.Update(first, true, false, true));
        Assert.Equal(new(InputCommand.Start, false), input.Update(second, true, false, true));
        Assert.Equal(new(InputCommand.Stop, false), input.Update(released, false, false, true));
        Assert.Equal(InputCommand.None, input.Update(released == first ? second : first, false, false, true).Command);
    }

    [Fact]
    public void ReleasingOneOfTwoPhysicalModifiersStopsEvenWhenTheOtherIsStillDown()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        input.Update(0xA2, true, false, true);
        input.Update(0xA3, true, false, true);
        Assert.Equal(InputCommand.Start, input.Update(0xA0, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0xA1, true, false, true).Command);
        Assert.Equal(InputCommand.Stop, input.Update(0xA2, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0xA3, true, false, true).Command);
        Assert.False(input.IsHoldingShortcut);
    }

    [Theory]
    [InlineData(0x11, 0x12, 0x20)]
    [InlineData(0x11, 0x20, 0x12)]
    [InlineData(0x12, 0x11, 0x20)]
    [InlineData(0x12, 0x20, 0x11)]
    [InlineData(0x20, 0x11, 0x12)]
    [InlineData(0x20, 0x12, 0x11)]
    public void CustomShortcutStartsOnceInAnyKeyOrderAndStopsOnAnyFirstRelease(int first, int second, int last)
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, Shortcut = ShortcutSpec.Parse("Ctrl+Alt+Space") };
        Assert.Equal(InputCommand.None, input.Update(first, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(second, true, false, true).Command);
        Assert.Equal(new(InputCommand.Start, last == 0x20), input.Update(last, true, false, true));
        Assert.Equal(InputCommand.Stop, input.Update(first, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(second, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(last, false, false, true).Command);
    }

    [Fact]
    public void PartialReleaseAndRepressCannotRearmBeforeTheWholeOriginalChordIsNeutral()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        input.Update(0x11, true, false, true);
        Assert.Equal(InputCommand.Start, input.Update(0x10, true, false, true).Command);
        Assert.Equal(InputCommand.Stop, input.Update(0x11, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x11, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x10, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x10, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x11, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x10, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x11, true, false, true).Command);
        Assert.Equal(InputCommand.Start, input.Update(0x10, true, false, true).Command);
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    public void AnExtraKeyStopsTheHoldWithoutSuppressingThatKeyOrRestarting(int extra)
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        input.Update(0x11, true, false, true);
        input.Update(0x10, true, false, true);
        Assert.Equal(new(InputCommand.Stop, false), input.Update(extra, true, true, true));
        Assert.False(input.IsHoldingShortcut);
        Assert.Equal(InputCommand.None, input.Update(extra, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x10, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x11, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x10, false, false, true).Command);
    }

    [Fact]
    public void APreexistingExtraKeyPreventsStartAndItsReleaseCannotStartRecording()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        input.Update(0x41, true, false, true);
        input.Update(0x11, true, false, true);
        Assert.Equal(InputCommand.None, input.Update(0x10, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x41, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x10, true, false, true).Command);
        Assert.False(input.IsHoldingShortcut);
    }

    [Fact]
    public void AnExistingRecordingNeverBecomesOwnedWhenItsSessionFlagChanges()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        input.Update(0x11, true, true, true);
        Assert.Equal(InputCommand.None, input.Update(0x10, true, true, true).Command);
        Assert.False(input.IsHoldingShortcut);
        Assert.Equal(InputCommand.None, input.Update(0x10, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x11, false, true, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x10, false, true, true).Command);
        input.Update(0x11, true, false, true);
        Assert.Equal(InputCommand.Start, input.Update(0x10, true, false, true).Command);
    }

    [Fact]
    public void DisablingAStartedHoldStillEmitsStopAndKeepsThePrimaryKeySuppressed()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, Shortcut = ShortcutSpec.Parse("Ctrl+Space") };
        input.Update(0x11, true, false, true);
        input.Update(0x20, true, false, true);
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, true, false, false));
        Assert.Equal(new(InputCommand.Stop, true), input.Update(0x20, false, false, false));
        Assert.Equal(InputCommand.None, input.Update(0x11, false, false, false).Command);
    }

    [Fact]
    public void EnablingTheShortcutMidGestureDoesNotAdoptAnAlreadyHeldChord()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, Shortcut = ShortcutSpec.Parse("Ctrl+Space") };
        input.Update(0x11, true, false, false);
        Assert.Equal(new(InputCommand.None, false), input.Update(0x20, true, false, false));
        Assert.Equal(InputCommand.None, input.Update(0x20, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x20, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x20, true, false, true).Command);
        Assert.False(input.IsHoldingShortcut);
    }

    [Theory]
    [InlineData("Mode")]
    [InlineData("Shortcut")]
    [InlineData("Capture")]
    [InlineData("CaptureRoundtrip")]
    public void ConfigurationAndCaptureResetsCannotStrandALiveHold(string change)
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, Shortcut = ShortcutSpec.Parse("Ctrl+Alt+Space") };
        input.Update(0x11, true, false, true);
        input.Update(0x12, true, false, true);
        input.Update(0x20, true, false, true);
        switch (change)
        {
            case "Mode": input.Mode = ShortcutMode.Toggle; break;
            case "Shortcut": input.Shortcut = ShortcutSpec.Parse("Ctrl+Shift+D"); break;
            case "Capture": input.CapturingShortcut = true; break;
            case "CaptureRoundtrip": input.CapturingShortcut = true; input.CapturingShortcut = false; break;
        }
        Assert.True(input.IsHoldingShortcut);
        Assert.Equal(new(InputCommand.Stop, false), input.Update(0x11, false, false, false));
        Assert.False(input.IsHoldingShortcut);
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, true, false, false));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, false, false, false));
        Assert.Equal(InputCommand.None, input.Update(0x12, false, false, false).Command);
    }

    [Fact]
    public void CaptureModeNeverStartsOrCancelsDictation()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, CapturingShortcut = true };
        foreach (var key in new[] { 0x11, 0x10, 0x1B })
            Assert.Equal(new(InputCommand.None, false), input.Update(key, true, true, true));
        foreach (var key in new[] { 0x1B, 0x10, 0x11 })
            Assert.Equal(new(InputCommand.None, false), input.Update(key, false, true, true));
        Assert.False(input.IsHoldingShortcut);
        input.CapturingShortcut = false;
        input.Update(0x11, true, false, true);
        Assert.Equal(InputCommand.Start, input.Update(0x10, true, false, true).Command);
    }

    [Fact]
    public void LeavingCaptureWithKeysDownRequiresTheOriginalChordToReturnToNeutral()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, CapturingShortcut = true };
        input.Update(0x11, true, false, true);
        input.Update(0x10, true, false, true);
        input.CapturingShortcut = false;
        input.Update(0x11, false, false, true);
        Assert.Equal(InputCommand.None, input.Update(0x11, true, false, true).Command);
        input.Update(0x11, false, false, true);
        input.Update(0x10, false, false, true);
        input.Update(0x11, true, false, true);
        Assert.Equal(InputCommand.Start, input.Update(0x10, true, false, true).Command);
    }

    [Fact]
    public void ChangingConfigurationDuringAPartialChordRequiresNeutralBeforeStarting()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        input.Update(0x11, true, false, true);
        input.Shortcut = ShortcutSpec.Parse("Ctrl+Shift");
        Assert.Equal(InputCommand.None, input.Update(0x10, true, false, true).Command);
        input.Update(0x11, false, false, true);
        input.Update(0x10, false, false, true);
        input.Update(0x11, true, false, true);
        Assert.Equal(InputCommand.Start, input.Update(0x10, true, false, true).Command);
    }

    [Fact]
    public void ChangingModesClearsAnyArmedToggleGesture()
    {
        var input = new InputCommands { Shortcut = ShortcutSpec.Parse("Ctrl+Space") };
        input.Update(0x11, true, false, true);
        input.Update(0x20, true, false, true);
        input.Mode = ShortcutMode.Hold;
        input.Mode = ShortcutMode.Toggle;
        Assert.Equal(InputCommand.None, input.Update(0x20, false, false, true).Command);
    }

    [Fact]
    public void PartialPrimaryReleaseCannotLeakARepeatedShortcutKeyOrStartAgain()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, Shortcut = ShortcutSpec.Parse("Ctrl+Space") };
        input.Update(0x11, true, false, true);
        input.Update(0x20, true, false, true);
        Assert.Equal(new(InputCommand.Stop, true), input.Update(0x20, false, false, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, true, false, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, false, false, true));
        Assert.False(input.IsHoldingShortcut);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APrimaryKeyPressedBeforeItsModifiersKeepsItsBalancingKeyUp(bool repeat)
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, Shortcut = ShortcutSpec.Parse("Ctrl+Alt+Space") };
        Assert.Equal(new(InputCommand.None, false), input.Update(0x20, true, false, true));
        input.Update(0x11, true, false, true);
        Assert.Equal(new(InputCommand.Start, false), input.Update(0x12, true, false, true));
        if (repeat) Assert.Equal(new(InputCommand.None, true), input.Update(0x20, true, false, true));
        Assert.Equal(new(InputCommand.Stop, false), input.Update(0x20, false, false, true));
    }

    [Fact]
    public void EscapeCancelsAnActiveHoldOnceWithoutLaterRestartingIt()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        input.Update(0x11, true, false, true);
        input.Update(0x10, true, false, true);
        Assert.Equal(new(InputCommand.Cancel, true), input.Update(0x1B, true, true, true));
        Assert.False(input.IsHoldingShortcut);
        Assert.Equal(new(InputCommand.None, true), input.Update(0x1B, true, true, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x1B, false, false, true));
        Assert.Equal(InputCommand.None, input.Update(0x10, true, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x11, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x10, false, false, true).Command);
        input.Update(0x11, true, false, true);
        Assert.Equal(InputCommand.Start, input.Update(0x10, true, false, true).Command);
    }

    [Fact]
    public void EscapeDuringPendingStartupStopsTheOwnedHoldWithoutDependingOnTheSessionFlag()
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold };
        input.Update(0x11, true, false, true);
        input.Update(0x10, true, false, true);
        Assert.Equal(InputCommand.Stop, input.Update(0x1B, true, false, true).Command);
        Assert.False(input.IsHoldingShortcut);
        Assert.Equal(InputCommand.None, input.Update(0x10, false, false, true).Command);
        Assert.Equal(InputCommand.None, input.Update(0x11, false, false, true).Command);
    }

    [Theory]
    [InlineData("Mode")]
    [InlineData("Shortcut")]
    public void ReconfiguringAfterEscapePreservesItsConsumedRepeatsAndBalancingRelease(string change)
    {
        var input = new InputCommands { Mode = ShortcutMode.Hold, Shortcut = ShortcutSpec.Parse("Ctrl+Space") };
        input.Update(0x11, true, false, true);
        input.Update(0x20, true, false, true);
        Assert.Equal(new(InputCommand.Cancel, true), input.Update(0x1B, true, true, true));
        if (change == "Mode") input.Mode = ShortcutMode.Toggle;
        else input.Shortcut = ShortcutSpec.Parse("Ctrl+Shift+D");
        Assert.Equal(new(InputCommand.None, true), input.Update(0x1B, true, true, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x1B, false, false, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, true, false, true));
        Assert.Equal(new(InputCommand.None, true), input.Update(0x20, false, false, true));
        Assert.Equal(InputCommand.None, input.Update(0x11, false, false, true).Command);
        Assert.Equal(new(InputCommand.None, false), input.Update(0x1B, true, false, true));
        Assert.Equal(new(InputCommand.None, false), input.Update(0x1B, false, false, true));
    }
}
