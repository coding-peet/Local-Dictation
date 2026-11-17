namespace LocalDictation.Core;

public readonly record struct UnicodeStroke(ushort Scan,bool KeyUp);

/// <summary>Plans UTF-16 unit pairs only; it does not send input.</summary>
public static class UnicodeTypingPlan
{
    public static UnicodeStroke[] Create(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for(var i=0;i<text.Length;i++)
        {
            if(char.IsControl(text[i])||text[i] is '\u2028' or '\u2029')
                throw new ArgumentException("Typing requires text without control characters or line separators.",nameof(text));
            if(char.IsHighSurrogate(text[i]))
            {
                if(i+1>=text.Length||!char.IsLowSurrogate(text[i+1]))
                    throw new ArgumentException("Typing requires paired UTF-16 surrogates.",nameof(text));
                i++;
            }
            else if(char.IsLowSurrogate(text[i]))
                throw new ArgumentException("Typing requires paired UTF-16 surrogates.",nameof(text));
        }
        var strokes=new UnicodeStroke[checked(text.Length*2)];
        for(var i=0;i<text.Length;i++)
        {
            strokes[2*i]=new(text[i],false);
            strokes[2*i+1]=new(text[i],true);
        }
        return strokes;
    }
}
