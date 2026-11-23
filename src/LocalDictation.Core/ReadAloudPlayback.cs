using System.Security.Cryptography;

namespace LocalDictation.Core;

/// <summary>Owns one read-aloud request and output on the caller's UI thread.</summary>
public sealed class ReadAloudPlayback(Func<string, CancellationToken, Task<SynthesisAudio>> synthesize,
    Func<byte[], IHistoryAudioOutput> outputFactory) : IDisposable
{
    public const int MaxTextLength = 500;
    readonly HistoryAudioPlayback playback = new(outputFactory);
    CancellationTokenSource? currentRequest;
    bool disposed;

    public bool IsPlaying => playback.State == HistoryPlaybackState.Playing;
    public bool IsActive => currentRequest is not null || IsPlaying;

    public async Task ToggleAsync(Func<CancellationToken, Task<string?>> readText)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Refresh();
        if (IsActive) { Stop(); return; }

        using var request = new CancellationTokenSource();
        currentRequest = request;
        try
        {
            var text = (await readText(request.Token))?.Trim();
            if (request.IsCancellationRequested) return;
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("There is no text to read aloud.");
            if (text.Length > MaxTextLength) throw new InvalidOperationException($"Choose up to {MaxTextLength} characters to read aloud.");
            var audio = await synthesize(text, request.Token);
            if (request.IsCancellationRequested)
            {
                CryptographicOperations.ZeroMemory(audio.Wave);
                return;
            }
            playback.Load(audio.Wave, sampleRate: 24000);
            try { playback.Play(); }
            catch { playback.Clear(); throw; }
        }
        catch (Exception) when (request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(currentRequest, request)) currentRequest = null; }
    }

    public void Refresh()
    {
        if (!playback.HasAudio) return;
        playback.Refresh();
        if (!IsPlaying) playback.Clear();
    }

    public void Stop()
    {
        var request = currentRequest;
        currentRequest = null;
        request?.Cancel();
        try { playback.Stop(); }
        finally { playback.Clear(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
    }
}
