using System.Buffers.Binary;

namespace LocalDictation.Core;

/// <summary>
/// Display energy from validated PCM16 on a fixed dBFS scale. Each time bucket
/// uses RMS energy: -54 dBFS and below is the display floor, -12 dBFS and above
/// fills the available height, and -24 dBFS occupies about 71%. These are display
/// references, not speech detection; wind and speech at the same energy look alike.
/// No recording-wide gain or peak normalization changes another bucket's scale.
/// </summary>
static class HistoryAudioEnvelope
{
    public static float[] Create(ReadOnlySpan<byte> pcm)
    {
        var samples=pcm.Length/2;
        var envelope=new float[Math.Min(256,samples)];
        for(var bucket=0;bucket<envelope.Length;bucket++)
        {
            var start=(int)((long)bucket*samples/envelope.Length);
            var end=(int)((long)(bucket+1)*samples/envelope.Length);
            double energy=0;
            for(var i=start;i<end;i++)
            {
                var sample=BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i*2,2))/32768d;
                energy+=sample*sample;
            }
            // 10 log10(mean square) is 20 log10(RMS); digital silence stays zero.
            if(energy>0)
                envelope[bucket]=(float)Math.Clamp((10*Math.Log10(energy/(end-start))+54)/42,0,1);
        }
        return envelope;
    }
}
