using LocalDictation.Core;
using NAudio.Wave;

namespace LocalDictation.App.Services;

/// <summary>Fresh, sequential in-memory WAV playback generations, created only on Play.</summary>
public sealed class HistoryAudioOutput : IHistoryAudioOutput,IHistoryAudioRetirement
{
    readonly RestartableHistoryAudioOutput output;
    public HistoryAudioOutput(byte[] wave)=>output=new(position=>new WaveOutSegment(wave,position));
    public TimeSpan Position{get=>output.Position;set=>output.Position=value;}
    public bool IsPlaying=>output.IsPlaying;
    public Task WhenRetired=>output.WhenRetired;
    public void Play()=>output.Play();
    public void Pause()=>output.Pause();
    public void Stop()=>output.Stop();
    public void Dispose()=>output.Dispose();

    sealed class WaveOutSegment : IHistoryAudioSegment,IHistoryAudioRetirement
    {
        readonly WaveFileReader reader;
        readonly WaveOut player;
        readonly TaskCompletionSource stopped=new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource retired=new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly object lifecycle=new();
        Exception? failure;
        int started,disposeRequested,disposed,drainFailed;
        public Task WhenRetired=>retired.Task;

        public WaveOutSegment(byte[] wave,TimeSpan position)
        {
            reader=new WaveFileReader(new MemoryStream(wave,false));reader.CurrentTime=position;
            WaveOut? opened=null;
            try
            {
                // NAudio captures this context for PlaybackStopped. Receive completion
                // directly on its worker so owner-thread Stop can wait without a UI deadlock.
                var context=SynchronizationContext.Current;
                try{SynchronizationContext.SetSynchronizationContext(null);opened=new WaveOut();}
                finally{SynchronizationContext.SetSynchronizationContext(context);}
                opened.Init(reader);opened.PlaybackStopped+=Stopped;player=opened;
            }
            catch{try{opened?.Dispose();}finally{reader.Dispose();}throw;}
        }

        void Stopped(object? sender,StoppedEventArgs args)
        {
            if(args.Exception!=null)Interlocked.CompareExchange(ref failure,args.Exception,null);
            // NAudio raises this after DoPlayback exits; no further source reads follow.
            stopped.TrySetResult();
            if(Volatile.Read(ref disposeRequested)!=0&&Volatile.Read(ref drainFailed)!=0)
                try{DisposeResources();}catch{/* Best-effort cleanup after a failed bounded drain. */}
        }

        void CheckFailure()
        {
            if(Volatile.Read(ref failure) is {} error)throw new InvalidOperationException("The audio output stopped unexpectedly.",error);
        }

        public bool IsPlaying
        {
            get
            {
                CheckFailure();
                return player.PlaybackState==PlaybackState.Playing||
                    player.PlaybackState==PlaybackState.Stopped&&Volatile.Read(ref started)!=0&&!stopped.Task.IsCompleted;
            }
        }

        public TimeSpan Position
        {
            get
            {
                CheckFailure();
                // This device is fresh for this segment; its counter is relative to zero.
                return TimeSpan.FromSeconds(player.GetPosition()/(double)reader.WaveFormat.AverageBytesPerSecond);
            }
        }

        public void Play()
        {
            CheckFailure();
            if(Interlocked.Exchange(ref started,1)!=0)throw new InvalidOperationException("A playback generation can only start once.");
            try{player.Play();}catch{stopped.TrySetResult();throw;}
        }

        public void Pause(){CheckFailure();player.Pause();}

        public void StopAndDrain()
        {
            lock(lifecycle)
            {
                if(Volatile.Read(ref disposed)!=0)return;
                if(Volatile.Read(ref drainFailed)!=0&&!stopped.Task.IsCompleted)
                    throw new InvalidOperationException("The previous audio output has not finished stopping.");
                player.Stop();
                if(Volatile.Read(ref started)!=0&&!stopped.Task.Wait(TimeSpan.FromSeconds(2)))
                {
                    Interlocked.Exchange(ref drainFailed,1);
                    throw new InvalidOperationException("The audio output did not finish stopping. A replacement will not start.");
                }
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref disposeRequested,1);
            if(Volatile.Read(ref drainFailed)!=0&&!stopped.Task.IsCompleted)return;
            try{StopAndDrain();}
            finally{if(Volatile.Read(ref started)==0||stopped.Task.IsCompleted)DisposeResources();}
        }

        void DisposeResources()
        {
            lock(lifecycle)
            {
                if(Volatile.Read(ref disposed)!=0)return;
                player.PlaybackStopped-=Stopped;
                try{player.Dispose();}
                finally{try{reader.Dispose();}finally{Volatile.Write(ref disposed,1);retired.TrySetResult();}}
            }
        }
    }
}
