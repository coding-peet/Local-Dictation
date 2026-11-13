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
    sealed record OwnedRange(string Text, int Start, int Caret, string Owned, bool HasApplied);
    OwnedRange range = new(text, caret, caret, "", false);
    public string Identity { get; } = identity;
    public string ExpectedText => range.Text;
    public int Start => range.Start;
    public int Caret => range.Caret;
    public string OwnedText => range.Owned;
    public bool HasApplied => range.HasApplied;
    public bool Valid { get; private set; } = true;
    public bool CanUseInitialEmptyProjection(string? projection,string currentText,int caret) =>
        Valid&&!HasApplied&&projection is not null&&currentText==projection&&caret==0;
    public void Invalidate() => Valid = false;
    public bool CanResume(string currentIdentity,string currentText)=>
        Valid&&currentIdentity==Identity&&currentText==range.Text;
    public bool CanReplace(string currentIdentity, string currentText, int selectionStart, int selectionEnd)
    {
        var current=range;
        return Valid&&currentIdentity==Identity&&currentText==current.Text&&
            selectionStart==current.Caret&&selectionEnd==current.Caret;
    }
    public string ReplacedText(string next)
    {
        var current=range;
        return current.Text[..current.Start]+next+current.Text[(current.Start+current.Owned.Length)..];
    }
    public void Applied(string inserted, string expected, int caretAfter)
    {
        range=new(expected,range.Start,caretAfter,inserted,true);
    }

    /// <summary>
    /// Adopts only the complete payload already observed after one known native paste.
    /// This is post-input ownership proof, never permission to treat a nonempty field as empty.
    /// The caller must prove unchanged focus/window/provider and no range selection.
    /// </summary>
    public bool TryAcknowledgeFirstPaste(FirstPasteEvidence evidence, string inserted,
        string observedIdentity, string observedText, int observedCaret)
    {
        var current=range;
        if(!Valid||current.HasApplied||current.Owned!=""||current.Text.Length==0||
            evidence.Identity!=Identity||observedIdentity!=Identity||evidence.Text!=current.Text||
            evidence.Caret!=current.Caret||current.Start!=current.Caret||
            (current.Caret!=0&&current.Caret!=current.Text.Length)||evidence.SelectionMade||
            !evidence.NativeClipboardPaste||evidence.QueuedInputEvents!=4||
            !evidence.IsKnownProvider||
            string.IsNullOrEmpty(inserted)||inserted.Any(char.IsControl)||
            observedText!=inserted||observedCaret!=inserted.Length||observedText==current.Text)
            return false;

        // One reference update publishes every part of the rebased ownership together.
        // HasApplied is monotonic, including after an acknowledged withdrawal.
        range=new(observedText,0,observedCaret,inserted,true);
        return true;
    }
}
