using System.Buffers.Binary;
using LocalDictation.Core;
using Xunit;

public class HistoryAudioWaveformTests
{
    [Fact] public void DigitalSilenceHasNoDisplayedEnergy()
    {
        using var playback=Load(25600,_=>0);
        Assert.All(playback.Peaks,value=>Assert.Equal(0,value));
    }

    [Fact] public void LowLevelNoiseStaysAtTheFloorInsteadOfFillingTheWaveform()
    {
        // Alternating PCM16 +/-32 is about -60 dBFS RMS.
        using var playback=Load(25600,i=>(short)(i%2==0?32:-32));
        Assert.All(playback.Peaks,value=>Assert.Equal(0,value));
    }

    [Fact] public void OrdinarySpeechLevelIsVisibleOnTheFixedDecibelScale()
    {
        // +/-2048 is -24.08 dBFS RMS, rather than 6.25% of a linear peak scale.
        using var playback=Load(25600,i=>(short)(i%2==0?2048:-2048));
        Assert.All(playback.Peaks,value=>Assert.InRange(value,.68f,.75f));
    }

    [Fact] public void QuietSpeechLevelRemainsVisibleWithoutAmplifyingTheNoiseFloor()
    {
        // +/-512 is -36.12 dBFS RMS: quieter than normal speech, still readable.
        using var playback=Load(25600,i=>(short)(i%2==0?512:-512));
        Assert.All(playback.Peaks,value=>Assert.InRange(value,.4f,.46f));
    }

    [Fact] public void AnIsolatedLoudBurstDoesNotRescaleSpeechInOtherTimeBuckets()
    {
        using var ordinary=Load(25600,i=>(short)(i%2==0?2048:-2048));
        using var burst=Load(25600,i=>i<100?(short)(i%2==0?32767:-32767):(short)(i%2==0?2048:-2048));
        Assert.Equal(1,burst.Peaks[0]);
        Assert.Equal(ordinary.Peaks.Skip(1),burst.Peaks.Skip(1));
        Assert.All(burst.Peaks.Skip(1),value=>Assert.InRange(value,.68f,.75f));
    }

    [Fact] public void AOneSampleSpikeShowsItsBucketEnergyRatherThanItsRawPeak()
    {
        // One full-scale sample among 400 samples has RMS 0.05, about -26 dBFS.
        using var playback=Load(102400,i=>i==0?short.MaxValue:(short)0);
        Assert.InRange(playback.Peaks[0],.64f,.69f);
        Assert.All(playback.Peaks.Skip(1),value=>Assert.Equal(0,value));
    }

    static HistoryAudioPlayback Load(int count,Func<int,short> sample)
    {
        var wave=new byte[44+count*2];"RIFF"u8.CopyTo(wave);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4),wave.Length-8);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8));BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16),16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20),1);BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22),1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24),16000);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28),32000);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32),2);BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34),16);
        "data"u8.CopyTo(wave.AsSpan(36));BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40),count*2);
        for(var i=0;i<count;i++)BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(44+i*2),sample(i));
        var playback=new HistoryAudioPlayback(_=>throw new InvalidOperationException("Waveform analysis must not open an output device."));
        playback.Load(wave);return playback;
    }
}
