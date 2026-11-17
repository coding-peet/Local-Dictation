using LocalDictation.Core;
using Xunit;

public class HistoryAudioOutputLifecycleTests
{
    [Fact] public void NaturalCompletionAndReplayUseAFreshCounterEvenWhenStopDoesNotResetIt()
    {
        var segments=new List<FakeSegment>();var starts=new List<TimeSpan>();
        using var output=new RestartableHistoryAudioOutput(start=>{starts.Add(start);var segment=new FakeSegment();segments.Add(segment);return segment;});
        output.Play();segments[0].Position=TimeSpan.FromSeconds(5);segments[0].IsPlaying=false;
        Assert.Equal(TimeSpan.FromSeconds(5),output.Position);
        output.Position=TimeSpan.Zero;output.Play();
        Assert.Equal(TimeSpan.Zero,output.Position);
        Assert.Equal(new[]{TimeSpan.Zero,TimeSpan.Zero},starts);
        Assert.True(segments[0].Drained);Assert.True(segments[0].Disposed);
    }

    [Fact] public void SeekingAPlayingSegmentDrainsAndDisposesItBeforeCreatingTheReplacement()
    {
        FakeSegment? previous=null;var starts=new List<TimeSpan>();
        using var output=new RestartableHistoryAudioOutput(start=>
        {
            if(previous!=null){Assert.True(previous.Drained);Assert.True(previous.Disposed);}
            starts.Add(start);return previous=new FakeSegment();
        });
        output.Play();output.Position=TimeSpan.FromSeconds(3);
        Assert.Equal(new[]{TimeSpan.Zero,TimeSpan.FromSeconds(3)},starts);
        Assert.True(output.IsPlaying);
        previous!.Position=TimeSpan.FromSeconds(.5);
        Assert.Equal(TimeSpan.FromSeconds(3.5),output.Position);
    }

    [Fact] public void PauseRetainsAudiblePositionAndResumeStartsAFreshSegmentThere()
    {
        var segments=new List<FakeSegment>();var starts=new List<TimeSpan>();
        using var output=new RestartableHistoryAudioOutput(start=>{starts.Add(start);var segment=new FakeSegment();segments.Add(segment);return segment;});
        output.Play();segments[0].Position=TimeSpan.FromSeconds(1.75);output.Pause();
        Assert.False(output.IsPlaying);Assert.Equal(TimeSpan.FromSeconds(1.75),output.Position);
        output.Position=TimeSpan.FromSeconds(2.5);
        Assert.Single(segments);output.Play();
        Assert.Equal(TimeSpan.FromSeconds(2.5),starts[1]);Assert.Equal(TimeSpan.FromSeconds(2.5),output.Position);
    }

    [Fact] public void StopResetsLogicalPositionWithoutTrustingTheOldCounter()
    {
        var segments=new List<FakeSegment>();
        using var output=new RestartableHistoryAudioOutput(_=>{var segment=new FakeSegment();segments.Add(segment);return segment;});
        output.Play();segments[0].Position=TimeSpan.FromSeconds(8);segments[0].IsPlaying=false;
        output.Stop();Assert.Equal(TimeSpan.Zero,output.Position);Assert.True(segments[0].Disposed);
        output.Play();Assert.Equal(TimeSpan.Zero,output.Position);Assert.Equal(2,segments.Count);
    }

    [Fact] public void AGenerationThatCannotDrainPreventsAReplacementFromStarting()
    {
        var segment=new FakeSegment{FailDrain=true};var created=0;
        var output=new RestartableHistoryAudioOutput(_=>{created++;return segment;});
        output.Play();
        Assert.Throws<InvalidOperationException>(()=>output.Position=TimeSpan.FromSeconds(1));
        Assert.Throws<InvalidOperationException>(output.Play);Assert.Equal(1,created);
        segment.FailDrain=false;output.Dispose();Assert.True(segment.Disposed);
    }

    sealed class FakeSegment : IHistoryAudioSegment
    {
        public TimeSpan Position {get;set;}
        public bool IsPlaying {get;set;}
        public bool Drained {get;private set;}
        public bool Disposed {get;private set;}
        public bool FailDrain {get;set;}
        public void Play()=>IsPlaying=true;
        public void Pause()=>IsPlaying=false;
        public void StopAndDrain(){if(FailDrain)throw new InvalidOperationException("Worker has not exited.");IsPlaying=false;Drained=true;}
        public void Dispose(){Assert.True(Drained);Disposed=true;}
    }
}
