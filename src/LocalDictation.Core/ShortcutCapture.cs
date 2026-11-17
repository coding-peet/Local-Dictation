namespace LocalDictation.Core;

public readonly record struct ShortcutCaptureResult(bool Complete,bool Cancelled,ShortcutSpec? Shortcut,string Error);

/// <summary>One key gesture, committed only after every captured key is released.</summary>
public sealed class ShortcutCapture
{
    readonly HashSet<int> down=[],gesture=[];
    bool releasing,invalid;
    ShortcutCaptureResult result;
    public bool Complete=>result.Complete;
    public string Preview=>string.Join('+',Tokens());
    public ShortcutCaptureResult Update(int key,bool pressed)
    {
        if(Complete)return result;
        if(pressed&&key is 0x1B or 0x09)return result=new(true,true,null,"");
        if(pressed)
        {
            if(!down.Add(key))return new(false,false,null,"");
            if(releasing)invalid=true;
            gesture.Add(key);
        }
        else
        {
            if(!down.Remove(key))return new(false,false,null,"");
            releasing=true;
            if(down.Count==0)
            {
                if(!invalid)
                    try{return result=new(true,false,ShortcutSpec.Parse(Preview),"");}
                    catch(ArgumentException){}
                return result=new(true,false,null,"Use Ctrl+Shift, or Ctrl/Shift/Alt with Space, a letter, digit, or F1–F24.");
            }
        }
        return new(false,false,null,"");
    }
    IEnumerable<string> Tokens()
    {
        if(gesture.Any(k=>k is 0x11 or 0xA2 or 0xA3))yield return "Ctrl";
        if(gesture.Any(k=>k is 0x10 or 0xA0 or 0xA1))yield return "Shift";
        if(gesture.Any(k=>k is 0x12 or 0xA4 or 0xA5))yield return "Alt";
        foreach(var key in gesture.Where(k=>!ShortcutSpec.IsModifier(k)))
            yield return key==0x20?"Space":key is >=0x70 and <=0x87?"F"+(key-0x6F):
                key is >=0x30 and <=0x39 or >=0x41 and <=0x5A?((char)key).ToString():"Unsupported key";
    }
}
