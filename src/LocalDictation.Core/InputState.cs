namespace LocalDictation.Core;

/// <summary>Toggle once on release of a modifier-only Ctrl+Shift chord.</summary>
public sealed class ModifierChord
{
    readonly HashSet<int> down = [];
    bool armed, other;
    static bool Ctrl(int key) => key is 0x11 or 0xA2 or 0xA3;
    static bool Shift(int key) => key is 0x10 or 0xA0 or 0xA1;
    public bool Update(int key, bool pressed)
    {
        if (pressed)
        {
            if (!down.Add(key)) return false;
            if (!Ctrl(key) && !Shift(key)) other = true;
            if (down.Any(Ctrl) && down.Any(Shift)) armed = true;
            return false;
        }
        down.Remove(key);
        var trigger = armed && !other && (Ctrl(key) || Shift(key));
        if (Ctrl(key) || Shift(key)) armed = false;
        if (!down.Any(Ctrl) && !down.Any(Shift)) other = false;
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
    public bool CanReplace(string currentIdentity, string currentText, int selectionStart, int selectionEnd) =>
        Valid && currentIdentity == Identity && currentText == ExpectedText &&
        selectionStart == Caret && selectionEnd == Caret;
    public string ReplacedText(string next) => ExpectedText[..Start] + next + ExpectedText[(Start + OwnedText.Length)..];
    public void Applied(string inserted, string expected, int caretAfter)
    {
        OwnedText = inserted; ExpectedText = expected; Caret = caretAfter;
    }
}
