using System.Buffers.Binary;
using LocalDictation.Core;
using Xunit;

public class HistoryAudioPlaybackTests
{
    [Fact] public void SelectingAudioBuildsItsRealEnvelopeWithoutCreatingAnOutputDevice()
    {
        var outputs=0;
        using var playback=new HistoryAudioPlayback(_=>{outputs++;throw new InvalidOperationException();});
        playback.Load(Wave(16000,i=>i<8000?(short)0:short.MinValue));
        Assert.Equal(0,outputs);
        Assert.Equal(TimeSpan.FromSeconds(1),playback.Duration);
        Assert.Equal(HistoryPlaybackState.Stopped,playback.State);
        Assert.Equal(256,playback.Peaks.Count);
        Assert.Equal(0,playback.Peaks[0]);
        Assert.Equal(1,playback.Peaks[^1]);
    }

    [Fact] public void PauseSeekAndResumeKeepTheSelectedPosition()
    {
        var output=new FakeOutput();
        using var playback=new HistoryAudioPlayback(_=>output);
        playback.Load(Wave(64000,_=>1000));
        playback.Play();output.Position=TimeSpan.FromSeconds(1.25);playback.Pause();
        Assert.Equal(HistoryPlaybackState.Paused,playback.State);
        Assert.Equal(TimeSpan.FromSeconds(1.25),playback.Position);
        playback.Seek(TimeSpan.FromSeconds(2.5));
        Assert.Equal(HistoryPlaybackState.Paused,playback.State);
        Assert.Equal(TimeSpan.FromSeconds(2.5),output.Position);
        playback.Play();
        Assert.Equal(HistoryPlaybackState.Playing,playback.State);
        Assert.Equal(TimeSpan.FromSeconds(2.5),output.Position);
    }

    [Fact] public void SeekingDuringPlaybackContinuesAndStopResetsToTheBeginning()
    {
        var output=new FakeOutput();
        using var playback=new HistoryAudioPlayback(_=>output);
        playback.Load(Wave(64000,_=>1000));playback.Play();
        playback.Seek(TimeSpan.FromSeconds(3));
        Assert.Equal(HistoryPlaybackState.Playing,playback.State);
        Assert.Equal(TimeSpan.FromSeconds(3),output.Position);
        playback.Stop();
        Assert.Equal(HistoryPlaybackState.Stopped,playback.State);
        Assert.Equal(TimeSpan.Zero,playback.Position);
        Assert.Equal(TimeSpan.Zero,output.Position);
        playback.Play();
        Assert.Equal(TimeSpan.Zero,output.Position);
    }

    [Fact] public void SeekingBeforePlayNeverOpensADeviceAndClampsToTheRecording()
    {
        var opened=false;
        using var playback=new HistoryAudioPlayback(_=>{opened=true;return new FakeOutput();});
        playback.Load(Wave(16000,_=>0));
        playback.Seek(TimeSpan.FromSeconds(3));
        Assert.Equal(playback.Duration,playback.Position);
        playback.Seek(TimeSpan.FromSeconds(-2));
        Assert.Equal(TimeSpan.Zero,playback.Position);
        Assert.False(opened);
    }

    [Fact] public void RefreshShowsOutputProgressAndCompletedPlaybackCanReplay()
    {
        var output=new FakeOutput();
        using var playback=new HistoryAudioPlayback(_=>output);
        playback.Load(Wave(32000,_=>10));playback.Play();
        output.Position=TimeSpan.FromSeconds(.75);playback.Refresh();
        Assert.Equal(TimeSpan.FromSeconds(.75),playback.Position);
        output.Position=playback.Duration;output.IsPlaying=false;playback.Refresh();
        Assert.Equal(HistoryPlaybackState.Stopped,playback.State);
        Assert.Equal(playback.Duration,playback.Position);
        playback.Play();
        Assert.Equal(TimeSpan.Zero,output.Position);
        Assert.Equal(HistoryPlaybackState.Playing,playback.State);
    }

    [Fact] public void ReplacingAndClosingAudioDisposeTheOutputAndZeroDecryptedBuffers()
    {
        var output=new FakeOutput();var first=Wave(16000,_=>25000);var second=Wave(32000,_=>10000);
        var playback=new HistoryAudioPlayback(_=>output);
        playback.Load(first);var firstPeaks=playback.Peaks;playback.Play();
        playback.Load(second);
        Assert.True(output.Disposed);
        Assert.All(first,value=>Assert.Equal(0,value));
        Assert.All(firstPeaks,value=>Assert.Equal(0,value));
        Assert.Equal(TimeSpan.FromSeconds(2),playback.Duration);
        playback.Dispose();
        Assert.All(second,value=>Assert.Equal(0,value));
        Assert.Equal(HistoryPlaybackState.Empty,playback.State);
    }

    [Fact] public void InvalidWaveIsRejectedAndItsBufferIsCleared()
    {
        var wave=new byte[]{1,2,3,4};
        using var playback=new HistoryAudioPlayback(_=>new FakeOutput());
        Assert.Throws<InvalidDataException>(()=>playback.Load(wave));
        Assert.All(wave,value=>Assert.Equal(0,value));
        Assert.Equal(HistoryPlaybackState.Empty,playback.State);
    }

    [Fact] public void AnOutputFailureReleasesTheDeviceAndLeavesAudioAvailableForRetry()
    {
        var output=new FakeOutput{FailPlay=true};var wave=Wave(16000,_=>1234);
        using var playback=new HistoryAudioPlayback(_=>output);
        playback.Load(wave);
        Assert.Throws<InvalidOperationException>(()=>playback.Play());
        Assert.True(output.Disposed);
        Assert.Equal(HistoryPlaybackState.Stopped,playback.State);
        Assert.True(playback.HasAudio);
        Assert.Contains(wave,value=>value!=0);
    }

    [Fact] public void FailedRetirementBlocksOuterFactoryUntilTheOldGenerationDrains()
    {
        var created=0;var segment=new BarrierSegment();
        using var playback=new HistoryAudioPlayback(_=>{created++;return new RestartableHistoryAudioOutput(_=>segment);});
        playback.Load(Wave(16000,_=>1000));playback.Play();segment.FailDrain=true;
        Assert.Throws<InvalidOperationException>(()=>playback.Seek(TimeSpan.FromSeconds(.5)));
        Assert.Throws<InvalidOperationException>(playback.Play);Assert.Equal(1,created);
        segment.FailDrain=false;playback.Play();Assert.Equal(2,created);
    }

    [Fact] public void ReselectionKeepsTheRetirementBarrierAndInUseAudioUntilSafelyReleased()
    {
        var created=0;var segment=new BarrierSegment();var first=Wave(16000,_=>1000);var second=Wave(16000,_=>2000);
        using var playback=new HistoryAudioPlayback(_=>{created++;return new RestartableHistoryAudioOutput(_=>segment);});
        playback.Load(first);playback.Play();segment.FailDrain=true;
        Assert.Throws<InvalidOperationException>(()=>playback.Seek(TimeSpan.FromSeconds(.5)));
        playback.Load(second);
        Assert.Contains(first,value=>value!=0);
        Assert.Throws<InvalidOperationException>(playback.Play);Assert.Equal(1,created);
        segment.FailDrain=false;playback.Play();
        Assert.All(first,value=>Assert.Equal(0,value));Assert.Equal(2,created);
        Assert.Contains(second,value=>value!=0);
    }

    [Fact] public void ClosingDuringFailedDrainZeroesTheOldWaveOnlyAfterAsyncRetirement()
    {
        var segment=new BarrierSegment();var wave=Wave(16000,_=>3000);
        var playback=new HistoryAudioPlayback(_=>new RestartableHistoryAudioOutput(_=>segment));
        playback.Load(wave);playback.Play();segment.FailDrain=true;playback.Dispose();
        Assert.Equal(HistoryPlaybackState.Empty,playback.State);
        Assert.Contains(wave,value=>value!=0);
        segment.CompleteRetirement();
        Assert.True(SpinWait.SpinUntil(()=>wave.All(value=>value==0),TimeSpan.FromSeconds(1)));
    }

    sealed class BarrierSegment : IHistoryAudioSegment,IHistoryAudioRetirement
    {
        readonly TaskCompletionSource retired=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WhenRetired=>retired.Task;
        public TimeSpan Position=>TimeSpan.Zero;
        public bool IsPlaying {get;private set;}
        public bool FailDrain {get;set;}
        public void Play()=>IsPlaying=true;
        public void Pause()=>IsPlaying=false;
        public void StopAndDrain(){if(FailDrain)throw new InvalidOperationException("Still reading its source.");IsPlaying=false;}
        public void Dispose(){if(FailDrain)throw new InvalidOperationException("Still reading its source.");retired.TrySetResult();}
        public void CompleteRetirement(){FailDrain=false;IsPlaying=false;retired.TrySetResult();}
    }

    static byte[] Wave(int count,Func<int,short> sample)
    {
        var wave=new byte[44+count*2];"RIFF"u8.CopyTo(wave);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4),wave.Length-8);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8));BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16),16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20),1);BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22),1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24),16000);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28),32000);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32),2);BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34),16);
        "data"u8.CopyTo(wave.AsSpan(36));BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40),count*2);
        for(var i=0;i<count;i++)BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(44+i*2),sample(i));
        return wave;
    }

    sealed class FakeOutput : IHistoryAudioOutput
    {
        public TimeSpan Position {get;set;}
        public bool IsPlaying {get;set;}
        public bool Disposed {get;private set;}
        public bool FailPlay {get;init;}
        public void Play(){if(FailPlay)throw new InvalidOperationException("Output unavailable.");IsPlaying=true;}
        public void Pause()=>IsPlaying=false;
        public void Stop()=>IsPlaying=false;
        public void Dispose(){Disposed=true;IsPlaying=false;}
    }
}
