using System.Buffers.Binary;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

public sealed record MicrophoneInfo(string Id, string Name)
{
    public override string ToString()=>Name;
}

public sealed class MicrophoneCapture : IAsyncDisposable
{
    readonly MMDeviceEnumerator enumerator = new();
    MMDevice? device;
    WasapiRecorder? recorder;
    readonly AudioContinuity continuity=new();
    readonly AudioBudget budget=new(600L*32000);
    bool limitSignaled;
    readonly MemoryStream historyAudio=new();
    readonly object audioGate=new();
    public string DeviceName {get;private set;}="Microphone";
    public event Action? LimitReached;
    public Channel<byte[]> Frames { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256)
    {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
    });
    public float Level { get; private set; }
    public long CapturedBytes { get; private set; }
    public event Action<string>? Faulted;

    public static List<MicrophoneInfo> Devices()
    {
        using var e = new MMDeviceEnumerator();
        using var devices = e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        return devices.Select(d => new MicrophoneInfo(d.ID, d.FriendlyName)).ToList();
    }

    public void Start(string? id)
    {
        device = string.IsNullOrEmpty(id)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            : enumerator.GetDevice(id);
        DeviceName=device.FriendlyName;
        recorder = new WasapiRecorderBuilder().WithDevice(device)
            .WithFormat(new WaveFormat(16000, 16, 1)).WithBufferLength(30).Build();
        recorder.DataAvailable += (buffer, flags, position, qpcPosition) =>
        {
            if(buffer.Length==0)return;
            if (!continuity.Accept(buffer.Length,(uint)flags))
            {
                Fail("The microphone stream has a gap. Stop and check the selected device.");
                return;
            }
            var accepted=budget.Accept(buffer.Length);
            var bytes = buffer[..accepted].ToArray();
            if (((uint)flags & 2) != 0) Array.Clear(bytes);
            float peak = 0;
            for (var i = 0; i + 1 < bytes.Length; i += 2)
                peak = Math.Max(peak, Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i, 2)) / 32768f));
            Level = peak;
            CapturedBytes += bytes.Length;
            lock(audioGate)historyAudio.Write(bytes);
            if (bytes.Length>0&&!Frames.Writer.TryWrite(bytes))
            {
                Array.Clear(bytes);
                Fail("The API cannot keep up with microphone audio. Your received text is in Review.");
                return;
            }
            if(budget.Reached&&!limitSignaled)
            {
                limitSignaled=true;Frames.Writer.TryComplete();LimitReached?.Invoke();
                _=Task.Run(()=>recorder?.StopRecording());
            }
        };
        recorder.RecordingStopped += (_, e) =>
        {
            Level = 0;
            Frames.Writer.TryComplete(e.Exception);
            if (e.Exception != null) Faulted?.Invoke("Microphone recording stopped unexpectedly. Check the device and Windows microphone access.");
        };
        recorder.StartRecording();
    }

    void Fail(string message)
    {
        if (Frames.Writer.TryComplete(new IOException(message)))
        {
            Faulted?.Invoke(message);
            _ = Task.Run(() => recorder?.StopRecording());
        }
    }
    public void Stop() => recorder?.StopRecording();
    public byte[] TakeAudio()
    {
        lock(audioGate)
        {
            var bytes=historyAudio.ToArray();
            if(historyAudio.TryGetBuffer(out var buffer))Array.Clear(buffer.Array!,buffer.Offset,buffer.Count);
            historyAudio.SetLength(0);return bytes;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (recorder != null) await recorder.DisposeAsync();
        Frames.Writer.TryComplete();
        while (Frames.Reader.TryRead(out var frame)) Array.Clear(frame);
        device?.Dispose();
        enumerator.Dispose();
    }
}
