using System.Buffers.Binary;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LocalDictation.App.Services;

public sealed record MicrophoneInfo(string Id, string Name);

public sealed class MicrophoneCapture : IAsyncDisposable
{
    readonly MMDeviceEnumerator enumerator = new();
    MMDevice? device;
    WasapiRecorder? recorder;
    long? expectedPosition;
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
        recorder = new WasapiRecorderBuilder().WithDevice(device)
            .WithFormat(new WaveFormat(16000, 16, 1)).WithBufferLength(30).Build();
        recorder.DataAvailable += (buffer, flags, position, _) =>
        {
            var current = Convert.ToInt64(position);
            if (expectedPosition.HasValue && current != expectedPosition.Value)
            {
                Fail("The microphone stream has a gap. Stop and check the selected device.");
                return;
            }
            expectedPosition = current + buffer.Length / 2;
            var bytes = buffer.ToArray();
            if (((uint)flags & 2) != 0) Array.Clear(bytes);
            float peak = 0;
            for (var i = 0; i + 1 < bytes.Length; i += 2)
                peak = Math.Max(peak, Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i, 2)) / 32768f));
            Level = peak;
            CapturedBytes += bytes.Length;
            if (CapturedBytes > 600L * 32000)
            {
                Array.Clear(bytes);
                Fail("The 10-minute recording limit was reached.");
                return;
            }
            if (!Frames.Writer.TryWrite(bytes))
            {
                Array.Clear(bytes);
                Fail("The API cannot keep up with microphone audio. Your received text is in Review.");
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

    public async ValueTask DisposeAsync()
    {
        if (recorder != null) await recorder.DisposeAsync();
        Frames.Writer.TryComplete();
        while (Frames.Reader.TryRead(out var frame)) Array.Clear(frame);
        device?.Dispose();
        enumerator.Dispose();
    }
}
