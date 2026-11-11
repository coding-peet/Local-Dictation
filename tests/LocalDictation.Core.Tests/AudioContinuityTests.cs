using LocalDictation.Core;
using Xunit;

public class AudioContinuityTests
{
    [Fact] public void JabraStartupAndEmptyPacketsDoNotInterruptRecording()
    {
        var capture=new AudioContinuity();
        foreach(var packet in new[]{(248,1u),(0,0u),(320,0u),(0,0u),(320,0u),(0,0u),(320,0u)})
            Assert.True(capture.Accept(packet.Item1,packet.Item2));
    }
    [Fact] public void RealDiscontinuityAfterFirstPacketStopsCapture()
    {
        var capture=new AudioContinuity();
        Assert.True(capture.Accept(0,1));Assert.True(capture.Accept(320,1));
        Assert.True(capture.Accept(320,2));Assert.False(capture.Accept(320,1));
    }
}
