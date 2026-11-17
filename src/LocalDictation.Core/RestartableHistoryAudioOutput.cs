namespace LocalDictation.Core;

/// <summary>Completes only after the output no longer owns or reads its source.</summary>
public interface IHistoryAudioRetirement
{
    Task WhenRetired {get;}
}

/// <summary>A single fresh-device generation; Position is elapsed audible time within it.</summary>
public interface IHistoryAudioSegment : IDisposable
{
    TimeSpan Position {get;}
    bool IsPlaying {get;}
    void Play();
    void Pause();
    /// <summary>Stop and confirm that the playback worker no longer reads its source.</summary>
    void StopAndDrain();
}

/// <summary>
/// Owns sequential playback generations. Seek, pause and stop retire the old
/// generation before another can start; no counter-reset or worker-exit assumption.
/// The factory receives the absolute WAV position for a fresh reader/device.
/// </summary>
public sealed class RestartableHistoryAudioOutput(Func<TimeSpan,IHistoryAudioSegment> createSegment) : IHistoryAudioOutput,IHistoryAudioRetirement
{
    readonly TaskCompletionSource retired=new(TaskCreationOptions.RunContinuationsAsynchronously);
    IHistoryAudioSegment? segment;
    TimeSpan origin;
    bool faulted,disposed;
    public Task WhenRetired=>retired.Task;
    public bool IsPlaying=>segment?.IsPlaying==true;
    public TimeSpan Position
    {
        get=>origin+(segment?.Position??TimeSpan.Zero);
        set
        {
            CheckAvailable();var resume=IsPlaying;CloseSegment();origin=value<TimeSpan.Zero?TimeSpan.Zero:value;
            if(resume)Play();
        }
    }

    public void Play()
    {
        CheckAvailable();if(IsPlaying)return;
        if(segment!=null){var position=Position;CloseSegment();origin=position;}
        segment=createSegment(origin);
        try{segment.Play();}catch{faulted=true;CloseSegment();throw;}
    }

    public void Pause()
    {
        CheckAvailable();if(segment==null)return;
        segment.Pause();var position=Position;CloseSegment();origin=position;
    }

    public void Stop(){CloseSegment();origin=TimeSpan.Zero;}

    void CheckAvailable()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(faulted)throw new InvalidOperationException("The previous audio output could not be retired safely.");
    }

    void CloseSegment()
    {
        var closing=segment;if(closing==null)return;
        try{closing.StopAndDrain();closing.Dispose();segment=null;}
        catch
        {
            faulted=true;
            // Native implementations may defer resource cleanup until their worker
            // exits after a failed bounded drain; always request that cleanup.
            try{closing.Dispose();}catch{}
            throw;
        }
    }

    public void Dispose()
    {
        if(disposed)return;
        try{CloseSegment();disposed=true;retired.TrySetResult();}
        catch
        {
            if(segment is IHistoryAudioRetirement pending)
                _=pending.WhenRetired.ContinueWith(_=>retired.TrySetResult(),CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            throw;
        }
    }
}
