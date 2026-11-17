using LocalDictation.Core;
using Xunit;

public class ShortcutCaptureTests
{
    static ShortcutCaptureResult Gesture(params int[] keys)
    {
        var capture=new ShortcutCapture();
        foreach(var key in keys)capture.Update(key,true);
        ShortcutCaptureResult result=default;
        foreach(var key in keys.Reverse())result=capture.Update(key,false);
        return result;
    }
    [Theory]
    [InlineData(new[]{0xA2,0xA0},"Ctrl+Shift")]
    [InlineData(new[]{0xA3,0xA5,0x20},"Ctrl+Alt+Space")]
    [InlineData(new[]{0x11,0x10,0x44},"Ctrl+Shift+D")]
    [InlineData(new[]{0x12,0x78},"Alt+F9")]
    public void SavesSupportedChords(int[] keys,string expected)=>Assert.Equal(expected,Gesture(keys).Shortcut?.ToString());
    [Fact] public void WaitsForAllReleasesAndIgnoresRepeats()
    {
        var capture=new ShortcutCapture();capture.Update(0x11,true);capture.Update(0x20,true);capture.Update(0x20,true);
        Assert.False(capture.Update(0x11,false).Complete);
        var result=capture.Update(0x20,false);
        Assert.True(result.Complete);Assert.Equal("Ctrl+Space",result.Shortcut?.ToString());
    }
    [Theory]
    [InlineData(new[]{0x41})]
    [InlineData(new[]{0x11})]
    [InlineData(new[]{0x11,0x5B,0x44})]
    [InlineData(new[]{0x11,0x41,0x42})]
    public void RejectsUnsupportedChords(int[] keys)
    {var result=Gesture(keys);Assert.True(result.Complete);Assert.Null(result.Shortcut);Assert.False(result.Cancelled);Assert.NotEmpty(result.Error);}
    [Fact] public void EscapeCancels()
    {var capture=new ShortcutCapture();capture.Update(0x11,true);Assert.True(capture.Update(0x1B,true).Cancelled);}
    [Fact] public void RollingKeysDoNotBecomeAnAccidentalChord()
    {
        var capture=new ShortcutCapture();capture.Update(0x11,true);capture.Update(0x10,true);capture.Update(0x11,false);
        capture.Update(0x44,true);capture.Update(0x44,false);
        Assert.Null(capture.Update(0x10,false).Shortcut);
    }
    [Fact] public void CapturingCannotToggleOrCancelDictation()
    {
        var input=new InputCommands{CapturingShortcut=true};
        foreach(var key in new[]{0x11,0x10,0x1B})Assert.Equal(InputCommand.None,input.Update(key,true,true,true).Command);
        foreach(var key in new[]{0x1B,0x10,0x11})Assert.Equal(InputCommand.None,input.Update(key,false,true,true).Command);
        input.CapturingShortcut=false;
        input.Update(0x11,true,false,true);input.Update(0x10,true,false,true);
        Assert.Equal(InputCommand.Toggle,input.Update(0x10,false,false,true).Command);
    }
    [Fact] public void LeavingCaptureDoesNotToggleFromAnOldRelease()
    {
        var input=new InputCommands();input.Update(0x11,true,false,true);input.Update(0x10,true,false,true);
        input.CapturingShortcut=true;input.CapturingShortcut=false;
        Assert.Equal(InputCommand.None,input.Update(0x10,false,false,true).Command);
    }
}
