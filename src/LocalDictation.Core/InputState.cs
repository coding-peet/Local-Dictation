namespace LocalDictation.Core;

/// <summary>Toggle once when a modifier-only Ctrl+Shift chord ends.</summary>
public sealed class ModifierChord
{
    readonly HashSet<int> down=[];
    bool armed,contaminated;
    static bool Ctrl(int key)=>key is 0x11 or 0xA2 or 0xA3;
    static bool Shift(int key)=>key is 0x10 or 0xA0 or 0xA1;
    bool Combo=>down.Any(Ctrl)&&down.Any(Shift);
    public bool Update(int key,bool pressed)
    {
        var before=Combo;
        if(pressed)
        {
            if(!down.Add(key))return false;
            if(!Ctrl(key)&&!Shift(key)&&armed)contaminated=true;
            if(!before&&Combo){armed=true;contaminated=down.Any(k=>!Ctrl(k)&&!Shift(k));}
            return false;
        }
        down.Remove(key);
        var trigger=armed&&before&&!Combo&&!contaminated&&!down.Any(k=>!Ctrl(k)&&!Shift(k));
        if(before&&!Combo)armed=false;
        if(!down.Any(Ctrl)&&!down.Any(Shift))contaminated=false;
        return trigger;
    }
}

public sealed class TargetGuard(string identity, string text, int caret)
{
    public string Identity { get; } = identity;
    public string ExpectedText { get; private set; } = text;
    public int Start { get; } = caret;
    public int Caret { get; private set; } = caret;
    public string OwnedText { get; private set; } = "";
    public bool Valid { get; private set; } = true;
    public void Invalidate() => Valid = false;
    public bool CanResume(string currentIdentity,string currentText)=>
        Valid&&currentIdentity==Identity&&currentText==ExpectedText;
    public bool CanReplace(string currentIdentity, string currentText, int selectionStart, int selectionEnd) =>
        Valid && currentIdentity == Identity && currentText == ExpectedText &&
        selectionStart == Caret && selectionEnd == Caret;
    public string ReplacedText(string next) => ExpectedText[..Start] + next + ExpectedText[(Start + OwnedText.Length)..];
    public void Applied(string inserted, string expected, int caretAfter)
    {
        OwnedText = inserted; ExpectedText = expected; Caret = caretAfter;
    }
}
