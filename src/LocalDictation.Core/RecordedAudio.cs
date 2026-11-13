using System.Buffers.Binary;

namespace LocalDictation.Core;

public static class RecordedAudio
{
    public static byte[] PcmFromWave(byte[] wave)
    {
        if(wave.Length<44||!wave.AsSpan(0,4).SequenceEqual("RIFF"u8)||!wave.AsSpan(8,4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("The original recording is not a WAV file.");
        bool format=false;int dataOffset=-1,dataLength=0;
        for(var offset=12;offset+8<=wave.Length;)
        {
            var size=BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(offset+4));
            if(size>int.MaxValue||size>wave.Length-offset-8)throw new InvalidDataException("The saved recording is truncated.");
            var length=(int)size;var chunk=wave.AsSpan(offset+8,length);
            if(wave.AsSpan(offset,4).SequenceEqual("fmt "u8))
                format=length>=16&&BinaryPrimitives.ReadInt16LittleEndian(chunk)==1&&BinaryPrimitives.ReadInt16LittleEndian(chunk[2..])==1&&
                    BinaryPrimitives.ReadInt32LittleEndian(chunk[4..])==16000&&BinaryPrimitives.ReadInt16LittleEndian(chunk[12..])==2&&BinaryPrimitives.ReadInt16LittleEndian(chunk[14..])==16;
            if(wave.AsSpan(offset,4).SequenceEqual("data"u8)){dataOffset=offset+8;dataLength=length;}
            offset+=8+length+(length&1);
        }
        if(!format||dataOffset<0||dataLength==0||dataLength%2!=0||dataLength>600*32000)
            throw new InvalidDataException("Expected a complete 16 kHz mono PCM16 recording, up to ten minutes.");
        return wave.AsSpan(dataOffset,dataLength).ToArray();
    }
}
