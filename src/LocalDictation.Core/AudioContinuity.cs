namespace LocalDictation.Core;

/// <summary>WASAPI reports empty packets without valid frame positions. Use its discontinuity flag.</summary>
public sealed class AudioContinuity
{
    bool received;
    public bool Accept(int byteCount,uint flags)
    {
        if(byteCount==0)return true;
        var valid=!received||(flags&1)==0;
        received=true;return valid;
    }
}
