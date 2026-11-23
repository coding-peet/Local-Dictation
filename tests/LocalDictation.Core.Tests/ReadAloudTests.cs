using System.Buffers.Binary;
using LocalDictation.Core;
using Xunit;

public sealed class ReadAloudTests
{
    [Fact]
    public void ReadAloudChordSuppressesRepeatAndLeavesTheRecordingShortcutIndependent()
    {
        var input = new InputCommands { ReadAloudEnabled = true };
        input.Update(0xA2, true, false, true);
        input.Update(0xA4, true, false, true);
        for (var press = 0; press < 2; press++)
        {
            Assert.Equal(new(InputCommand.None, true), input.Update('R', true, false, true));
            Assert.Equal(new(InputCommand.None, true), input.Update('R', true, false, true));
            Assert.Equal(new(InputCommand.ReadAloud, true), input.Update('R', false, false, true));
        }
        input.Update(0xA4, false, false, true);
        input.Update(0xA0, true, false, true);
        Assert.Equal(InputCommand.Toggle, input.Update(0xA0, false, false, true).Command);
    }

    [Fact]
    public void AltGrTypingDoesNotStartReading()
    {
        var input = new InputCommands { ReadAloudEnabled = true };
        input.Update(0xA2, true, false, true);
        input.Update(0xA5, true, false, true);
        Assert.Equal(new(InputCommand.None, false), input.Update('R', true, false, true));
        Assert.Equal(new(InputCommand.None, false), input.Update('R', false, false, true));
    }

    [Fact]
    public async Task SecondInvocationStopsActualPlaybackOwnershipWithoutReadingAnotherSelection()
    {
        var wave = Sample();
        var output = new Output();
        using var reader = new ReadAloudPlayback((text, _) =>
        {
            Assert.Equal("Ahoj", text);
            return Task.FromResult(new SynthesisAudio(wave, "cpu"));
        }, _ => output);
        await reader.ToggleAsync(_ => Task.FromResult<string?>(" Ahoj "));
        Assert.True(output.IsPlaying);
        await reader.ToggleAsync(_ => throw new InvalidOperationException("Stop must not read the source."));
        Assert.False(reader.IsActive);
        Assert.False(output.IsPlaying);
        Assert.True(output.Disposed);
        Assert.All(wave, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task SecondInvocationDuringSynthesisDiscardsEvenALateSuccessfulResponse()
    {
        var reply = new TaskCompletionSource<SynthesisAudio>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wave = Sample();
        var token = CancellationToken.None;
        var opened = false;
        using var reader = new ReadAloudPlayback((_, cancellation) => { token = cancellation; return reply.Task; },
            _ => { opened = true; return new Output(); });
        var first = reader.ToggleAsync(_ => Task.FromResult<string?>("Ahoj"));
        Assert.True(reader.IsActive);
        await reader.ToggleAsync(_ => throw new InvalidOperationException("Stop must not read the source."));
        Assert.True(token.IsCancellationRequested);
        reply.SetResult(new(wave, "cpu"));
        await first;
        Assert.False(reader.IsActive);
        Assert.False(opened);
        Assert.All(wave, value => Assert.Equal(0, value));
    }

    static byte[] Sample()
    {
        var wav = new byte[46];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 38);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), 24000);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 48000);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), 2);
        return wav;
    }

    sealed class Output : IHistoryAudioOutput
    {
        public TimeSpan Position { get; set; }
        public bool IsPlaying { get; private set; }
        public bool Disposed { get; private set; }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { Disposed = true; IsPlaying = false; }
    }
}
