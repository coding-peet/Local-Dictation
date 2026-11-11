namespace LocalDictation.Core;

public sealed record ShortcutSpec(bool Ctrl,bool Shift,bool Alt,int Key)
{
    public static ShortcutSpec Parse(string value)
    {
        var parts=value.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        bool ctrl=false,shift=false,alt=false;int key=0;
        foreach(var raw in parts)
        {
            var part=raw.ToUpperInvariant();
            if(part=="CTRL"&&!ctrl)ctrl=true;
            else if(part=="SHIFT"&&!shift)shift=true;
            else if(part=="ALT"&&!alt)alt=true;
            else if(key==0&&part=="SPACE")key=0x20;
            else if(key==0&&part.Length==1&&char.IsAsciiLetterOrDigit(part[0]))key=part[0];
            else if(key==0&&part.StartsWith('F')&&int.TryParse(part[1..],out var f)&&f is >=1 and <=24)key=0x6F+f;
            else throw new ArgumentException("Use Ctrl+Shift, or modifiers with Space, a letter, digit, or F1–F24.");
        }
        if(!(ctrl||shift||alt)||key==0&&!(ctrl&&shift&&!alt))
            throw new ArgumentException("Use at least one modifier and a key, or Ctrl+Shift.");
        return new(ctrl,shift,alt,key);
    }
    public override string ToString()=>string.Join('+',new[]{Ctrl?"Ctrl":null,Shift?"Shift":null,Alt?"Alt":null,
        Key==0?null:Key==0x20?"Space":Key is >=0x70 and <=0x87?"F"+(Key-0x6F):((char)Key).ToString()}.Where(p=>p!=null));
    public static bool IsModifier(int key)=>key is 0x10 or 0x11 or 0x12 or >=0xA0 and <=0xA5;
    public bool Matches(ISet<int> down)=>
        Ctrl==down.Any(k=>k is 0x11 or 0xA2 or 0xA3)&&
        Shift==down.Any(k=>k is 0x10 or 0xA0 or 0xA1)&&
        Alt==down.Any(k=>k is 0x12 or 0xA4 or 0xA5);
}
