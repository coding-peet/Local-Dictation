namespace LocalDictation.Core;

/// <summary>Facts captured at the final validation before one collapsed native paste.</summary>
public readonly record struct FirstPasteEvidence(
    string? ProcessName, string FrameworkId, string ClassName, int ControlTypeId,
    string Identity, string Text, int Caret, bool SelectionMade,
    bool NativeClipboardPaste, int QueuedInputEvents)
{
    public bool IsKnownProvider
    {
        get
        {
            // FrameworkId identifies the provider. Codex and other Chromium hosts
            // need not use chrome.exe; the caller validates the captured native window.
            if(FrameworkId!="Chrome"||ControlTypeId!=50004||ClassName is null)return false;
            var tokens=ClassName.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length==1&&tokens[0]=="ProseMirror"||
                tokens.Length==2&&tokens.Contains("ProseMirror",StringComparer.Ordinal)&&
                tokens.Contains("ProseMirror-focused",StringComparer.Ordinal);
        }
    }
}
