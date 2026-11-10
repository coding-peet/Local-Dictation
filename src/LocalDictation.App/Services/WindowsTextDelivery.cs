using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

/// <summary>Owns an initially empty selection; never restores focus or sends Enter.</summary>
public sealed class WindowsTextDelivery
{
    readonly nint window;
    readonly TargetGuard guard;
    int invalid;
    public string Reason { get; private set; } = "";
    public string InsertedText => guard.OwnedText;

    WindowsTextDelivery(nint window, TargetGuard guard) { this.window = window; this.guard = guard; }
    public void Invalidate() => Interlocked.Exchange(ref invalid, 1);

    static string Identity(AutomationElement element) =>
        element.Current.ProcessId + ":" + string.Join(".", element.GetRuntimeId());

    static (string Text, int Caret, TextPattern Pattern, TextPatternRange Selection) Inspect(AutomationElement element)
    {
        if (!element.Current.IsEnabled || element.Current.IsPassword)
            throw new InvalidOperationException("The selected field is disabled or protected.");
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var value))
            throw new InvalidOperationException("This field does not expose a safe text range. Continue in Review.");
        var pattern = (TextPattern)value;
        var readOnly = pattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        if (readOnly is not false)
            throw new InvalidOperationException("The selected field does not confirm editable text access.");
        var selections = pattern.GetSelection();
        if (selections.Length != 1 ||
            selections[0].CompareEndpoints(TextPatternRangeEndpoint.Start, selections[0], TextPatternRangeEndpoint.End) != 0)
            throw new InvalidOperationException("Place an empty cursor in an editable field before dictating.");
        var document = pattern.DocumentRange;
        var text = document.GetText(100001);
        if (text.Length > 100000) throw new InvalidOperationException("This document is too large for safe cursor validation.");
        var prefix = document.Clone();
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, selections[0], TextPatternRangeEndpoint.Start);
        var before = prefix.GetText(-1);
        if (!text.StartsWith(before, StringComparison.Ordinal))
            throw new InvalidOperationException("The field's text range could not be validated.");
        return (text, before.Length, pattern, selections[0]);
    }

    public static WindowsTextDelivery Capture(CancellationToken token, bool allowOwnProcess = false)
    {
        var window = GetForegroundWindow();
        var element = AutomationElement.FocusedElement ?? throw new InvalidOperationException("No editable field has focus.");
        if (!allowOwnProcess && element.Current.ProcessId == Environment.ProcessId)
            throw new InvalidOperationException("Choose a destination outside Local Dictation; this recording will open in Review.");
        if (Integrity(element.Current.ProcessId) > Integrity(Environment.ProcessId))
            throw new InvalidOperationException("The destination is elevated. Continue in Review.");
        var info = Inspect(element);
        token.ThrowIfCancellationRequested();
        if (window == 0 || GetForegroundWindow() != window ||
            Identity(AutomationElement.FocusedElement) != Identity(element))
            throw new InvalidOperationException("The destination changed during the cursor check.");
        return new(window, new(Identity(element), info.Text, info.Caret));
    }

    public bool Apply(string next, CancellationToken token)
    {
        try
        {
            Check(token);
            var element = AutomationElement.FocusedElement;
            var current = Inspect(element);
            if (!guard.CanReplace(Identity(element), current.Text, current.Caret, current.Caret))
                return Stop("The cursor or field content changed. Your complete text is in Review.");
            if (next == guard.OwnedText) return true;
            if (next.Any(c => char.IsControl(c)))
                return Stop("The transcript contains a control character. Copy it deliberately from Review.");
            var common = 0;
            while (common < next.Length && common < guard.OwnedText.Length && next[common] == guard.OwnedText[common]) common++;
            if (common > 0 && common < next.Length && char.IsLowSurrogate(next[common])) common--;
            var oldTail = guard.OwnedText[common..];
            var replacement = next[common..];
            var expected = guard.ReplacedText(next);
            TextPatternRange selected = current.Selection;
            if (oldTail.Length != 0)
            {
                var search = current.Pattern.DocumentRange.Clone();
                var found = false;
                for (var attempt = 0; attempt < 256; attempt++)
                {
                    var candidate = search.FindText(oldTail, false, false);
                    if (candidate == null) break;
                    var prefix = current.Pattern.DocumentRange.Clone();
                    prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, candidate, TextPatternRangeEndpoint.Start);
                    if (prefix.GetText(-1) == guard.ExpectedText[..(guard.Start + common)] &&
                        candidate.GetText(-1) == oldTail &&
                        candidate.CompareEndpoints(TextPatternRangeEndpoint.End, current.Selection, TextPatternRangeEndpoint.Start) == 0)
                    { selected = candidate; found = true; break; }
                    search.MoveEndpointByRange(TextPatternRangeEndpoint.Start, candidate, TextPatternRangeEndpoint.End);
                }
                if (!found) return Stop("The owned draft range is no longer available.");
            }
            Check(token);
            selected.Select();
            Check(token);
            // Only the mutable suffix is replaced. Stable leading text is untouched.
            if (replacement.Length == 0 && oldTail.Length != 0)
            {
                // Delete selection with a virtual Delete key; never issue Backspace guesses.
                SendKey(0x2E, token);
            }
            else
            {
                for (var offset = 0; offset < replacement.Length; offset += 128)
                {
                    Check(token);
                    SendUnicode(replacement.Substring(offset, Math.Min(128, replacement.Length - offset)));
                }
            }
            for (var attempt = 0; attempt < 15; attempt++)
            {
                Check(token);
                var afterElement = AutomationElement.FocusedElement;
                var after = Inspect(afterElement);
                if (Identity(afterElement) != guard.Identity) return Stop("The destination changed during insertion.");
                if (after.Text == expected && after.Caret == guard.Start + next.Length)
                {
                    guard.Applied(next, expected, after.Caret);
                    return true;
                }
                Thread.Sleep(10);
            }
            return Stop("The destination did not confirm the inserted text. Review the complete result before pasting.");
        }
        catch (OperationCanceledException) { return Stop("The cursor check was cancelled. Your text is in Review."); }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception)
        { return Stop("Safe cursor delivery is unavailable. Your complete text is in Review."); }
    }

    bool Stop(string message) { Reason = message; Invalidate(); return false; }
    void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref invalid) != 0 || GetForegroundWindow() != window ||
            Identity(AutomationElement.FocusedElement) != guard.Identity ||
            new[] {0x10,0x11,0x12,0x5B,0x5C}.Any(k => GetAsyncKeyState(k) < 0))
            throw new InvalidOperationException("The original cursor no longer owns delivery.");
    }

    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyInput Key; }
    static Input Key(ushort value, uint flags, bool unicode) => new()
    { Type = 1, Key = new() { Key = unicode ? (ushort)0 : value, Scan = unicode ? value : (ushort)0,
        Flags = flags | (unicode ? 4u : 0u), Extra = GlobalInput.InjectionTag } };
    static void SendUnicode(string text)
    {
        var inputs = text.SelectMany(c => new[] {Key(c,0,true),Key(c,2,true)}).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception("Windows rejected text delivery.");
    }
    static void SendKey(ushort key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Input[] inputs = [Key(key,0,false), Key(key,2,false)];
        if (SendInput(2, inputs, Marshal.SizeOf<Input>()) != 2) throw new Win32Exception("Windows rejected range replacement.");
    }

    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(nint token, int info, nint data, int size, out int required);
    [DllImport("advapi32.dll")] static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] static extern nint GetSidSubAuthority(nint sid, uint index);

    static int Integrity(int pid)
    {
        var process = OpenProcess(0x1000, false, pid);
        if (process == 0) throw new Win32Exception("Cannot validate destination privileges.");
        nint token = 0, data = 0;
        try
        {
            if (!OpenProcessToken(process, 8, out token)) throw new Win32Exception("Cannot validate destination token.");
            GetTokenInformation(token, 25, 0, 0, out var size);
            data = Marshal.AllocHGlobal(size);
            if (!GetTokenInformation(token, 25, data, size, out _)) throw new Win32Exception("Cannot validate destination integrity.");
            var sid = Marshal.ReadIntPtr(data);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        finally { if (data != 0) Marshal.FreeHGlobal(data); if (token != 0) CloseHandle(token); CloseHandle(process); }
    }
}
