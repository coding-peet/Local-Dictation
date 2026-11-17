using System.Security.Cryptography;

namespace LocalDictation.Core;

public enum HistoryPlaybackState { Empty,Stopped,Playing,Paused }

/// <summary>The only boundary that may open an audio device. Created on explicit Play.</summary>
public interface IHistoryAudioOutput : IDisposable
{
    TimeSpan Position {get;set;}
    bool IsPlaying {get;}
    void Play();
    void Pause();
    void Stop();
}

/// <summary>
/// Windowless playback state with an injectable output. Load takes ownership of the
/// decrypted WAV; replacing/clearing it zeroes that buffer and its derived envelope.
/// Call from one owner thread, including Refresh. No device is created by Load or Seek.
/// </summary>
public sealed class HistoryAudioPlayback(Func<byte[],IHistoryAudioOutput> outputFactory) : IDisposable
{
    byte[]? audio;
    float[] peaks=[];
    IHistoryAudioOutput? output;
    IHistoryAudioOutput? retiringOutput;
    byte[]? retiringAudio;
    public bool HasAudio=>audio!=null;
    public HistoryPlaybackState State {get;private set;}=HistoryPlaybackState.Empty;
    public TimeSpan Position {get;private set;}
    public TimeSpan Duration {get;private set;}
    /// <summary>Display-scaled RMS envelope; retained Peaks name for player compatibility.</summary>
    public IReadOnlyList<float> Peaks=>peaks;

    public void Load(byte[] wave)
    {
        ArgumentNullException.ThrowIfNull(wave);
        Clear();byte[]? pcm=null;
        try
        {
            pcm=RecordedAudio.PcmFromWave(wave);
            var samples=pcm.Length/2;
            peaks=HistoryAudioEnvelope.Create(pcm);
            Duration=TimeSpan.FromSeconds(samples/16000d);audio=wave;
            State=HistoryPlaybackState.Stopped;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(wave);Clear();throw;
        }
        finally{if(pcm!=null)CryptographicOperations.ZeroMemory(pcm);}
    }

    public void Play()
    {
        if(audio==null||State==HistoryPlaybackState.Playing)return;
        if(!TryRetireOutput())throw new InvalidOperationException("The previous audio output has not finished stopping.");
        try
        {
            output??=outputFactory(audio);
            if(Position>=Duration)Position=TimeSpan.Zero;
            output.Position=Position;output.Play();State=HistoryPlaybackState.Playing;
        }
        catch{ReleaseOutput();State=HistoryPlaybackState.Stopped;throw;}
    }

    public void Pause()
    {
        if(State!=HistoryPlaybackState.Playing||output==null)return;
        try{output.Pause();Position=Clamp(output.Position);State=HistoryPlaybackState.Paused;}
        catch{ReleaseOutput();State=HistoryPlaybackState.Stopped;throw;}
    }

    public void Stop()
    {
        try{if(output!=null){output.Stop();output.Position=TimeSpan.Zero;}}
        catch{ReleaseOutput();throw;}
        finally{Position=TimeSpan.Zero;State=HasAudio?HistoryPlaybackState.Stopped:HistoryPlaybackState.Empty;}
    }

    public void Seek(TimeSpan position)
    {
        if(!HasAudio)return;
        var next=Clamp(position);
        try{if(output!=null)output.Position=next;Position=next;}
        catch{ReleaseOutput();State=HistoryPlaybackState.Stopped;throw;}
    }

    public void Refresh()
    {
        if(State!=HistoryPlaybackState.Playing||output==null)return;
        try
        {
            Position=Clamp(output.Position);
            if(!output.IsPlaying){Position=Duration;State=HistoryPlaybackState.Stopped;}
        }
        catch{ReleaseOutput();State=HistoryPlaybackState.Stopped;throw;}
    }

    TimeSpan Clamp(TimeSpan value)=>value<TimeSpan.Zero?TimeSpan.Zero:value>Duration?Duration:value;

    void ReleaseOutput()
    {
        if(output!=null){retiringOutput=output;retiringAudio=audio;output=null;}
        TryRetireOutput();
    }

    bool TryRetireOutput()
    {
        var old=retiringOutput;if(old==null)return true;
        try{old.Stop();}catch{/* Dispose may still establish safe retirement. */}
        try{old.Dispose();}
        catch
        {
            if(old is not IHistoryAudioRetirement pending||!pending.WhenRetired.IsCompletedSuccessfully)return false;
        }
        if(retiringAudio!=null&&!ReferenceEquals(retiringAudio,audio))CryptographicOperations.ZeroMemory(retiringAudio);
        retiringOutput=null;retiringAudio=null;return true;
    }

    public void Clear()
    {
        ReleaseOutput();
        if(audio!=null)
        {
            if(ReferenceEquals(audio,retiringAudio))
            {
                // The worker still owns this buffer. Zero it as soon as retirement
                // is confirmed, even when the closed control is never used again.
                if(retiringOutput is IHistoryAudioRetirement pending)
                {
                    var buffer=audio;
                    _=pending.WhenRetired.ContinueWith(_=>CryptographicOperations.ZeroMemory(buffer),CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                }
            }
            else CryptographicOperations.ZeroMemory(audio);
        }
        audio=null;
        Array.Clear(peaks);peaks=[];Position=Duration=TimeSpan.Zero;State=HistoryPlaybackState.Empty;
    }

    public void Dispose()=>Clear();
}
