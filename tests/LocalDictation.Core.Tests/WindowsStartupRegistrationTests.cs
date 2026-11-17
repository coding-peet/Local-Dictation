using LocalDictation.Core;
using Xunit;

public sealed class WindowsStartupRegistrationTests
{
    const string Executable = @"C:\Program Files\Local Dictation\LocalDictation.exe";
    const string Command = "\"C:\\Program Files\\Local Dictation\\LocalDictation.exe\" --startup";

    [Fact]
    public void MissingRegistrationIsOffAndReadingNeverWrites()
    {
        var store = new FakeStartupStore();
        var startup = new WindowsStartupRegistration(store, Executable);
        var state = startup.Read();
        Assert.False(state.Enabled);
        Assert.True(state.CanChange);
        Assert.Equal(0, store.Mutations);
        Assert.Null(store.Value);
    }

    [Fact]
    public void EnableQuotesTheActualExecutableAndVerifiesTheStoredCommand()
    {
        var store = new FakeStartupStore();
        var result = new WindowsStartupRegistration(store, Executable).SetEnabled(true);
        Assert.True(result.Success);
        Assert.True(result.State.Enabled);
        Assert.True(result.State.CanChange);
        Assert.Equal("", result.Error);
        Assert.Equal(Command, store.Value);
        Assert.Equal(1, store.Mutations);
    }

    [Fact]
    public void RunCommandAt260CharacterLimitIsAllowed()
    {
        var executable = @"C:\" + new string('a', 226) + @"\LocalDictation.exe";
        Assert.Equal(248, executable.Length);
        var store = new FakeStartupStore();
        var result = new WindowsStartupRegistration(store, executable).SetEnabled(true);
        Assert.True(result.Success);
        Assert.True(result.State.Enabled);
        Assert.True(result.State.CanChange);
        Assert.Equal(260, Assert.IsType<string>(store.Value).Length);
        Assert.Equal(1, store.Mutations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunCommandOver260CharacterLimitIsRejectedWithoutAccessingStore(bool hasRegistration)
    {
        var executable = @"C:\" + new string('a', 227) + @"\LocalDictation.exe";
        Assert.Equal(249, executable.Length);
        var existing = hasRegistration ? "\"" + executable + "\" --startup" : null;
        var store = new FakeStartupStore { Value = existing };
        var startup = new WindowsStartupRegistration(store, executable);
        var state = startup.Read();
        Assert.False(state.Enabled);
        Assert.False(state.CanChange);
        Assert.Contains("shorter path", state.Message, StringComparison.OrdinalIgnoreCase);
        var result = startup.SetEnabled(true);
        Assert.False(result.Success);
        Assert.False(result.State.Enabled);
        Assert.False(result.State.CanChange);
        Assert.Contains("shorter path", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(startup.SetEnabled(false).Success);
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Mutations);
        Assert.Equal(existing, store.Value);
    }

    [Fact]
    public void ExistingOwnedRegistrationIsEnabledWithoutMutation()
    {
        var store = new FakeStartupStore { Value = Command };
        var startup = new WindowsStartupRegistration(store, Executable);
        Assert.True(startup.Read().Enabled);
        Assert.True(startup.SetEnabled(true).Success);
        Assert.Equal(0, store.Mutations);
    }

    [Fact]
    public void DisableDeletesTheOwnedRegistrationAndIsIdempotent()
    {
        var store = new FakeStartupStore { Value = Command };
        var startup = new WindowsStartupRegistration(store, Executable);
        var first = startup.SetEnabled(false);
        Assert.True(first.Success);
        Assert.False(first.State.Enabled);
        Assert.Null(store.Value);
        Assert.True(startup.SetEnabled(false).Success);
        Assert.Equal(1, store.Mutations);
    }

    [Fact]
    public void CommandMatchingUsesWindowsPathCaseInsensitivity()
    {
        var store = new FakeStartupStore { Value = Command.ToUpperInvariant() };
        Assert.True(new WindowsStartupRegistration(store, Executable).Read().Enabled);
        Assert.Equal(0, store.Mutations);
    }

    [Theory]
    [InlineData("\"C:\\Other\\Other.exe\" --startup")]
    [InlineData("\"C:\\Old installation\\LocalDictation.exe\" --startup")]
    [InlineData("\"C:\\Program Files\\Local Dictation\\LocalDictation.exe\" --startup --another")]
    [InlineData("C:\\Program Files\\Local Dictation\\LocalDictation.exe --startup")]
    [InlineData(123)]
    public void ConflictingValueIsPreservedForBothToggleDirections(object existing)
    {
        var store = new FakeStartupStore { Value = existing };
        var startup = new WindowsStartupRegistration(store, Executable);
        var state = startup.Read();
        Assert.False(state.Enabled);
        Assert.False(state.CanChange);
        Assert.NotEmpty(state.Message);
        Assert.False(startup.SetEnabled(true).Success);
        Assert.False(startup.SetEnabled(false).Success);
        Assert.Equal(existing, store.Value);
        Assert.Equal(0, store.Mutations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LocalDictation.exe")]
    [InlineData(@"C:LocalDictation.exe")]
    [InlineData(@"C:\SDK\dotnet.exe")]
    [InlineData(@"C:\tests\testhost.exe")]
    [InlineData(@"C:\tests\VerificationBannerRender.exe")]
    [InlineData(@"C:\app\LocalDictation.dll")]
    [InlineData("\"C:\\app\\LocalDictation.exe\"")]
    [InlineData("C:\\bad\npath\\LocalDictation.exe")]
    [InlineData("C:\\bad\0path\\LocalDictation.exe")]
    public void InvalidOrHostExecutableCannotRegisterStartup(string? executable)
    {
        var store = new FakeStartupStore();
        var startup = new WindowsStartupRegistration(store, executable);
        Assert.False(startup.Read().CanChange);
        var result = startup.SetEnabled(true);
        Assert.False(result.Success);
        Assert.False(result.State.Enabled);
        Assert.NotEmpty(result.Error);
        Assert.Equal(0, store.Mutations);
    }

    [Fact]
    public void FirstReadFailureReportsUnknownStateInsteadOfClaimingVerifiedOff()
    {
        var store = new FakeStartupStore { FailRead = true };
        var startup = new WindowsStartupRegistration(store, Executable);
        var state = startup.Read();
        Assert.False(state.Enabled);
        Assert.False(state.CanChange);
        Assert.Contains("unknown", state.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(startup.SetEnabled(true).Success);
        Assert.Equal(0, store.Mutations);
    }

    [Fact]
    public void FailedReadRetainsLastVerifiedStateAndDisablesChanges()
    {
        var store = new FakeStartupStore { Value = Command };
        var startup = new WindowsStartupRegistration(store, Executable);
        Assert.True(startup.Read().Enabled);
        store.FailRead = true;
        var state = startup.Read();
        Assert.True(state.Enabled);
        Assert.False(state.CanChange);
        Assert.NotEmpty(state.Message);
        Assert.False(startup.SetEnabled(false).Success);
        Assert.Equal(Command, store.Value);
        Assert.Equal(0, store.Mutations);
    }

    [Fact]
    public void DeniedEnableReturnsVerifiedPreviousStateAndActionableSafeError()
    {
        var store = new FakeStartupStore { FailWrite = true };
        var result = new WindowsStartupRegistration(store, Executable).SetEnabled(true);
        Assert.False(result.Success);
        Assert.False(result.State.Enabled);
        Assert.True(result.State.CanChange);
        Assert.Null(store.Value);
        Assert.NotEmpty(result.Error);
        Assert.DoesNotContain("sensitive fixture", result.Error);
    }

    [Fact]
    public void DeniedDisableRetainsVerifiedEnabledState()
    {
        var store = new FakeStartupStore { Value = Command, FailDelete = true };
        var result = new WindowsStartupRegistration(store, Executable).SetEnabled(false);
        Assert.False(result.Success);
        Assert.True(result.State.Enabled);
        Assert.Equal(Command, store.Value);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void ErrorAfterACompletedWriteReturnsActualReadbackState()
    {
        var store = new FakeStartupStore { FailAfterWrite = true };
        var result = new WindowsStartupRegistration(store, Executable).SetEnabled(true);
        Assert.False(result.Success);
        Assert.True(result.State.Enabled);
        Assert.Equal(Command, store.Value);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void SilentlyRejectedWriteCannotReportSuccess()
    {
        var store = new FakeStartupStore { IgnoreWrite = true };
        var result = new WindowsStartupRegistration(store, Executable).SetEnabled(true);
        Assert.False(result.Success);
        Assert.False(result.State.Enabled);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void FailedReadbackRetainsLastVerifiedStateAndReportsUncertainty()
    {
        var store = new FakeStartupStore { FailReadAfterWrite = true };
        var result = new WindowsStartupRegistration(store, Executable).SetEnabled(true);
        Assert.False(result.Success);
        Assert.False(result.State.Enabled);
        Assert.False(result.State.CanChange);
        Assert.Equal(Command, store.Value);
        Assert.NotEmpty(result.Error);
        Assert.NotEmpty(result.State.Message);
    }

    [Fact]
    public void ForeignReplacementDuringChangeIsReportedAsConflict()
    {
        const string foreign = @"C:\Other\other.exe";
        var store = new FakeStartupStore { ReplacementOnWrite = foreign };
        var result = new WindowsStartupRegistration(store, Executable).SetEnabled(true);
        Assert.False(result.Success);
        Assert.False(result.State.Enabled);
        Assert.False(result.State.CanChange);
        Assert.Equal(foreign, store.Value);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void ToggleRechecksExternalChangesInsteadOfTrustingPreviousRead()
    {
        var store = new FakeStartupStore();
        var startup = new WindowsStartupRegistration(store, Executable);
        Assert.True(startup.Read().CanChange);
        store.Value = 123;
        Assert.False(startup.SetEnabled(true).Success);
        Assert.Equal(123, store.Value);
        Assert.Equal(0, store.Mutations);
    }

    sealed class FakeStartupStore : IWindowsStartupStore
    {
        public object? Value;
        public bool FailRead, FailWrite, FailDelete, FailAfterWrite, IgnoreWrite, FailReadAfterWrite;
        public string? ReplacementOnWrite;
        public int Mutations, Reads;
        public object? Read()
        {
            Reads++;
            return FailRead ? throw new IOException("sensitive fixture") : Value;
        }
        public void Write(string command)
        {
            if (FailWrite) throw new UnauthorizedAccessException("sensitive fixture");
            Mutations++;
            if (!IgnoreWrite) Value = ReplacementOnWrite ?? command;
            if (FailReadAfterWrite) FailRead = true;
            if (FailAfterWrite) throw new IOException("sensitive fixture");
        }
        public void Delete()
        {
            if (FailDelete) throw new UnauthorizedAccessException("sensitive fixture");
            Mutations++;
            Value = null;
        }
    }
}
