using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

/// <summary>Owns the original draft range. Focus restoration requires explicit Review acceptance.</summary>
public sealed class WindowsTextDelivery : ITextDelivery
{
    readonly nint window;
    readonly TargetGuard guard;
    readonly AutomationElement original;
    readonly string provider;
    int invalid,operating,generation;
    public nint Window=>window;
    public bool IsConflict=>Volatile.Read(ref invalid)!=0;
    public static nint ForegroundWindow=>GetForegroundWindow();
    public string Reason { get; private set; } = "";
    public string InsertedText => guard.OwnedText;
    public DeliveryDiagnostic? LastDiagnostic {get;private set;}

    WindowsTextDelivery(nint window,TargetGuard guard,AutomationElement original)
    {this.window=window;this.guard=guard;this.original=original;provider=ProviderMetadata(original);}
    public void Invalidate() => Interlocked.Exchange(ref invalid, 1);
    public void Suspend()=>Interlocked.Increment(ref generation);
    public bool OwnsProgrammaticFocus(nint root)=>root==window&&Volatile.Read(ref operating)>0;

    static string Identity(AutomationElement? element) => element==null
        ?throw new InvalidOperationException("The focused field is temporarily unavailable.")
        :element.Current.ProcessId + ":" + string.Join(".", element.GetRuntimeId());

    static (string Text, int Caret, TextPattern Pattern, TextPatternRange Selection) Inspect(AutomationElement? element,Action<string>? observeStage=null)
    {
        observeStage?.Invoke("availability");
        if(element==null)throw new InvalidOperationException("The focused field is temporarily unavailable.");
        if (!element.Current.IsEnabled || element.Current.IsPassword)
            throw new InvalidOperationException("The selected field is disabled or protected.");
        observeStage?.Invoke("text-pattern");
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var value))
            throw new InvalidOperationException("This field does not expose a safe text range. Continue in Review.");
        var pattern = (TextPattern)value;
        observeStage?.Invoke("read-only");
        var readOnly = pattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        if (readOnly is not false)
            throw new InvalidOperationException("The selected field does not confirm editable text access.");
        observeStage?.Invoke("selection-count");
        var selections = pattern.GetSelection();
        if(selections.Length!=1)
            throw new InvalidOperationException("The field does not expose one current selection.");
        observeStage?.Invoke("selection-collapse");
        if(selections[0].CompareEndpoints(TextPatternRangeEndpoint.Start,selections[0],TextPatternRangeEndpoint.End)!=0)
            throw new InvalidOperationException("Place an empty cursor in an editable field before dictating.");
        observeStage?.Invoke("document-text");
        var document = pattern.DocumentRange;
        var text = document.GetText(100001);
        if (text.Length > 100000) throw new InvalidOperationException("This document is too large for safe cursor validation.");
        observeStage?.Invoke("caret-prefix");
        var prefix = document.Clone();
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, selections[0], TextPatternRangeEndpoint.Start);
        var before = prefix.GetText(-1);
        observeStage?.Invoke("prefix-coherence");
        if (!text.StartsWith(before, StringComparison.Ordinal))
            throw new InvalidOperationException("The field's text range could not be validated.");
        return (text, before.Length, pattern, selections[0]);
    }

    public static WindowsTextDelivery Capture(CancellationToken token, bool allowOwnProcess = false)
    {
        var diagnostic=CaptureDiagnostic.Begin();
        AutomationElement? element=null;
        try
        {
            diagnostic.Stage(CaptureStage.Foreground);
            var window = GetForegroundWindow();
            diagnostic.Stage(CaptureStage.FocusedElement);
            element = AutomationElement.FocusedElement ?? throw new InvalidOperationException("No editable field has focus.");
            diagnostic.Stage(CaptureStage.OwnProcess);
            if (!allowOwnProcess && element.Current.ProcessId == Environment.ProcessId)
                throw new InvalidOperationException("Choose a destination outside Local Dictation; this recording will open in Review.");
            diagnostic.Stage(CaptureStage.Integrity);
            if (!TargetPrivileges.CanInsert(Integrity(element.Current.ProcessId), Integrity(Environment.ProcessId)))
                throw new InvalidOperationException("The destination is elevated. Continue in Review.");
            var info = Inspect(element,stage=>diagnostic.Stage(stage switch
            {
                "availability"=>CaptureStage.Availability,"text-pattern"=>CaptureStage.TextPattern,
                "read-only"=>CaptureStage.ReadOnly,"selection-count"=>CaptureStage.SelectionCount,
                "selection-collapse"=>CaptureStage.SelectionCollapse,"document-text"=>CaptureStage.DocumentText,
                "caret-prefix"=>CaptureStage.CaretPrefix,"prefix-coherence"=>CaptureStage.PrefixCoherence,
                _=>CaptureStage.Unknown
            }));
            diagnostic.Stage(CaptureStage.Cancellation);
            token.ThrowIfCancellationRequested();
            diagnostic.Stage(CaptureStage.PostcheckWindow);
            if (window == 0 || GetForegroundWindow() != window)
                throw new InvalidOperationException("The destination changed during the cursor check.");
            diagnostic.Stage(CaptureStage.PostcheckFocusIdentity);
            if(Identity(AutomationElement.FocusedElement) != Identity(element))
                throw new InvalidOperationException("The destination changed during the cursor check.");
            diagnostic.Stage(CaptureStage.ConstructIdentity);
            var guard=new TargetGuard(Identity(element),info.Text,info.Caret);
            diagnostic.Stage(CaptureStage.ProviderMetadata);
            var delivery=new WindowsTextDelivery(window,guard,element);
            diagnostic.Complete();
            return delivery;
        }
        catch(Exception error)
        {
            diagnostic.Fail(error);
            if(element!=null)diagnostic.FailedProvider(()=>new(element.Current.FrameworkId,element.Current.ClassName,
                element.Current.ControlType.Id,element.Current.ProcessId,element.Current.HasKeyboardFocus,
                element.Current.IsKeyboardFocusable,element.GetSupportedPatterns().Select(pattern=>pattern.Id).ToArray()));
            throw;
        }
    }

    enum InterruptionStage { Invalidated,InputGeneration,ForegroundWindow,FocusedField,DeliveryKeysDown }
    sealed class Interrupted(InterruptionStage stage) : Exception
    {
        public string Stage=>stage switch
        {
            InterruptionStage.Invalidated=>"invalidated",InterruptionStage.InputGeneration=>"input-generation",
            InterruptionStage.ForegroundWindow=>"foreground-window",InterruptionStage.FocusedField=>"focused-field",
            InterruptionStage.DeliveryKeysDown=>"delivery-keys-down",_=>"unknown"
        };
    }
    public bool Apply(string next,CancellationToken token,bool restoreFocus=false)
    {
        if(IsConflict)return false;
        Reason="";
        if(next.Any(char.IsControl))
            return Pause("Remove line breaks or control characters from the final draft before inserting. Your original field and Review are unchanged.");
        var version=Volatile.Read(ref generation);
        var queued=0;
        var watch=Stopwatch.StartNew();
        var phase="validate";
        var beforeText=guard.ExpectedText;
        string? expected=null;
        int? observedLength=null,observedCaret=null;
        DeliveryTextMismatch? textMismatch=null;
        bool? textMatches=null,caretMatches=null,unchanged=null,lineEndingsEquivalent=null,terminalParagraphEquivalent=null,nbspEquivalent=null;
        ClipboardTransaction? clipboard=null;
        bool confirmed=false;
        string? failureType=null;int? failureCode=null;
        var readbackRetries=0;
        string? inspectionStage=null,lastReadbackStage=null,lastReadbackFailureType=null;
        string? lastInterruptionStage=null;
        int? lastReadbackFailureCode=null;
        Action<string> observeInspection=stage=>inspectionStage=stage;
        bool Reconcile()
        {
            if(expected==null)return false;
            int caret;
            try
            {
                Check(token,version);
                if(!TryConfirmLate(next,expected,out caret))return false;
                Check(token,version);
            }
            catch(Exception error) when(error is Interrupted or OperationCanceledException or InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception)
            {
                if(error is Interrupted interruption)lastInterruptionStage=interruption.Stage;
                return false;
            }
            guard.Applied(next,expected,caret);
            confirmed=true;clipboard?.Acknowledge();
            phase="late confirm";observedLength=expected.Length;observedCaret=guard.Start+next.Length;
            textMatches=caretMatches=lineEndingsEquivalent=terminalParagraphEquivalent=nbspEquivalent=true;
            unchanged=expected==beforeText;return true;
        }
        Interlocked.Increment(ref operating);
        try
        {
            if(restoreFocus)
            {
                // Keep held acceptance keys in Review. Moving focus first can
                // redirect their physical repeats to a destination's send action.
                if(!WaitForKeyRelease(DeliveryKeysDown,()=>CheckLease(token,version),()=>Thread.Sleep(10)))
                    return Pause("Release Enter, Space and modifier keys, then try Accept again. The destination was not activated.");
                if(!IsWindow(window)||Identity(original)!=guard.Identity)
                    return Stop("The original destination is no longer available. Choose another destination.");
                if(IsIconic(window))ShowWindow(window,9);
                if(!SetForegroundWindow(window)&&GetForegroundWindow()!=window)
                    return Pause("Windows could not return to the original window. Click that field and try Accept again.");
                original.SetFocus();
            }
            Check(token,version,false);
            // A modifier-only start/stop chord may still have one key held down.
            if(!WaitForKeyRelease(DeliveryKeysDown,()=>Check(token,version,false),()=>Thread.Sleep(10)))
                return Pause("Release Enter, Space and modifier keys before cursor delivery resumes.");
            Check(token,version);
            var element = AutomationElement.FocusedElement;
            var current = Inspect(element,observeInspection);
            if(!guard.CanResume(Identity(element),current.Text))
                return Stop("The original field was edited. Its contents were left untouched; choose a destination for the reviewed text.");
            if(current.Caret!=guard.Caret)
            {
                OwnedEnd(current.Pattern).Select();
                Check(token,version);current=Inspect(element,observeInspection);
                if(!guard.CanReplace(Identity(element),current.Text,current.Caret,current.Caret))
                    return Pause("The original insertion point could not be restored. Your transcript is preserved.");
            }
            if (next == guard.OwnedText) return true;
            var common = 0;
            while (common < next.Length && common < guard.OwnedText.Length && next[common] == guard.OwnedText[common]) common++;
            if (common > 0 && common < next.Length && char.IsLowSurrogate(next[common])) common--;
            var oldTail = guard.OwnedText[common..];
            var replacement = next[common..];
            expected = guard.ReplacedText(next);
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
            if(replacement.Length>0)
            {
                phase="clipboard backup";
                clipboard=ClipboardTransaction.Begin(replacement,()=>Check(token,version));
            }
            Check(token,version);
            // Appending already owns the current caret. Reselecting a collapsed
            // UIA range can invalidate an editor's active selection.
            if(oldTail.Length!=0)selected.Select();
            Check(token,version);
            phase="queue input";
            // Only the mutable suffix is replaced. Stable leading text is untouched.
            if (replacement.Length == 0 && oldTail.Length != 0)
            {
                // Delete selection with a virtual Delete key; never issue Backspace guesses.
                SendKey(0x2E, token,ref queued);
            }
            else if(clipboard!=null)
            {
                phase="clipboard ownership";clipboard.EnsureCurrent();
                phase="queue input";clipboard.MarkMayHaveQueued();
                // One ordinary paste command. No Enter or application send command is synthesized.
                QueueInput([Key(0x11,0,false),Key(0x56,0,false),Key(0x56,2,false),Key(0x11,2,false)],ref queued);
            }
            phase="confirm";
            var confirmation=Stopwatch.StartNew();
            while(confirmation.Elapsed<TimeSpan.FromSeconds(1))
            {
                try
                {
                    inspectionStage="focus-before-read";Check(token,version);
                    inspectionStage="focused-element";var afterElement=AutomationElement.FocusedElement;
                    var after=Inspect(afterElement,observeInspection);
                    inspectionStage="focus-after-read";Check(token,version);
                    if(Identity(afterElement)!=guard.Identity)return Stop("The destination changed during insertion.");
                    observedLength=after.Text.Length;observedCaret=after.Caret;
                    textMatches=after.Text==expected;caretMatches=after.Caret==guard.Start+next.Length;unchanged=after.Text==beforeText;
                    if(DiagnosticJournal.Enabled)textMismatch=DeliveryDiagnostic.DescribeTextMismatch(expected,after.Text,guard.Start+next.Length);
                    lineEndingsEquivalent=after.Text.Replace("\r\n","\n")==expected.Replace("\r\n","\n");
                    terminalParagraphEquivalent=after.Text.TrimEnd('\r','\n')==expected.TrimEnd('\r','\n');
                    nbspEquivalent=after.Text.Replace('\u00a0',' ')==expected.Replace('\u00a0',' ');
                    if(textMatches==true&&caretMatches==true)
                    {
                        confirmed=true;clipboard?.Acknowledge();
                        guard.Applied(next,expected,after.Caret);return true;
                    }
                }
                catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception)
                {
                    // A provider can expose an intermediate selection/range while applying paste.
                    // Retry only readback. No new input or target adoption occurs in this loop.
                    readbackRetries++;lastReadbackStage=inspectionStage;
                    lastReadbackFailureType=error.GetType().Name;lastReadbackFailureCode=error.HResult;
                    CheckLease(token,version);
                    if(GetForegroundWindow()!=window)throw new Interrupted(InterruptionStage.ForegroundWindow);
                }
                Thread.Sleep(10);
            }
            if(Reconcile())return true;
            return Stop("The destination did not confirm the inserted text. Check the destination before retrying; the complete transcript is preserved in Review.");
        }
        catch(Interrupted error)
        {
            lastInterruptionStage=error.Stage;failureType=error.GetType().Name;failureCode=error.HResult;
            return queued>0?(expected!=null&&Reconcile()||Stop("Insertion was interrupted. Check the destination before choosing where to insert the preserved Review.")):
            Pause("Cursor delivery is paused. Return to the original unchanged field to resume.");}
        catch(OperationCanceledException)
        {return queued>0?(expected!=null&&Reconcile()||Stop("Insertion confirmation was interrupted. Check the destination before retrying; the complete transcript is preserved in Review.")):Pause("Cursor validation was cancelled. Your text is preserved.");}
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception)
        {
            failureType=ex.GetType().Name;failureCode=ex.HResult;
            if(queued>0)return expected!=null&&Reconcile()||Stop("The inserted text could not be confirmed. Check your destination; the complete Review is preserved.");
            return Pause(phase is "clipboard backup" or "clipboard ownership"?ex.Message:phase=="queue input"?"Windows did not accept text delivery. Your transcript is preserved; return to the original field and try again.":"The original field could not be checked. Return to it or choose another destination.");
        }
        finally
        {
            try
            {
                if(queued==0)clipboard?.MarkNoInputQueued();
                if(confirmed)clipboard?.Acknowledge();
                clipboard?.Dispose();
                if(ClipboardRecovery.HasPending)Reason=ClipboardRecovery.Message;
            }
            catch(Exception error)
            {
                Reason="Text delivery finished, but Windows could not restore every previous clipboard format. Check the clipboard before copying again.";
                Diagnostics.Error("clipboard restore",error);
            }
            LastDiagnostic=new(phase,restoreFocus,queued,watch.Elapsed.TotalMilliseconds,beforeText.Length,next.Length,expected?.Length,
                observedLength,observedCaret,textMatches,caretMatches,unchanged,lineEndingsEquivalent,terminalParagraphEquivalent,nbspEquivalent,
                provider,clipboard?.SavedFormatCount,clipboard?.WasRestored,clipboard?.WasSuperseded,IsConflict,failureType,failureCode,ClipboardRecovery.HasPending,
                readbackRetries,lastReadbackStage,lastReadbackFailureType,lastReadbackFailureCode,lastInterruptionStage,textMismatch);
            LastDiagnostic.WriteIfEnabled();
            Interlocked.Decrement(ref operating);
        }
    }

    bool TryConfirmLate(string next,string expected,out int caret)
    {
        caret=0;
        // Read only the originally captured element. A late exact acknowledgement must not cause a second paste.
        try
        {
            if(!IsWindow(window)||Identity(original)!=guard.Identity)return false;
            var after=Inspect(original);
            if(after.Text!=expected||after.Caret!=guard.Start+next.Length)return false;
            caret=after.Caret;return true;
        }
        catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception){return false;}
    }
    static string ProviderMetadata(AutomationElement element)
    {
        try
        {
            static string Safe(string value)=>new(value.Where(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '.' or '-' or '#').Take(80).ToArray());
            return Safe(element.Current.FrameworkId)+"/"+Safe(element.Current.ClassName)+"/"+element.Current.ControlType.Id;
        }
        catch(Exception){return "unavailable";}
    }

    TextPatternRange OwnedEnd(TextPattern pattern)
    {
        var prefix=guard.ExpectedText[..guard.Caret];
        var range=pattern.DocumentRange.Clone();
        if(prefix.Length==0){range.MoveEndpointByRange(TextPatternRangeEndpoint.End,range,TextPatternRangeEndpoint.Start);return range;}
        var found=range.FindText(prefix,false,false);
        if(found==null||found.GetText(-1)!=prefix||found.CompareEndpoints(TextPatternRangeEndpoint.Start,range,TextPatternRangeEndpoint.Start)!=0)
            throw new InvalidOperationException("The original insertion anchor is no longer available.");
        found.MoveEndpointByRange(TextPatternRangeEndpoint.Start,found,TextPatternRangeEndpoint.End);return found;
    }
    bool Pause(string message){Reason=message;return false;}
    bool Stop(string message) { Reason = message; Invalidate(); return false; }
    static bool DeliveryKeysDown()=>new[]{0x10,0x11,0x12,0x5B,0x5C,0x0D,0x20}.Any(k=>GetAsyncKeyState(k)<0);
    internal static bool WaitForKeyRelease(Func<bool> keysDown,Action validate,Action wait)
    {
        validate();
        for(var attempt=0;keysDown();attempt++)
        {
            if(attempt==60)return false;
            wait();validate();
        }
        return true;
    }
    void CheckLease(CancellationToken token,int version)
    {
        token.ThrowIfCancellationRequested();
        if(IsConflict)throw new Interrupted(InterruptionStage.Invalidated);
        if(version!=Volatile.Read(ref generation))throw new Interrupted(InterruptionStage.InputGeneration);
    }
    void Check(CancellationToken token,int version,bool modifiers=true)
    {
        CheckLease(token,version);
        if(GetForegroundWindow()!=window)throw new Interrupted(InterruptionStage.ForegroundWindow);
        if(Identity(AutomationElement.FocusedElement)!=guard.Identity)throw new Interrupted(InterruptionStage.FocusedField);
        if(modifiers&&DeliveryKeysDown())throw new Interrupted(InterruptionStage.DeliveryKeysDown);
    }

    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyInput Key; }
    static Input Key(ushort value, uint flags, bool unicode) => new()
    { Type = 1, Key = new() { Key = unicode ? (ushort)0 : value, Scan = unicode ? value : (ushort)0,
        Flags = flags | (unicode ? 4u : 0u), Extra = GlobalInput.InjectionTag } };
    static void SendKey(ushort key,CancellationToken token,ref int queued)
    {
        token.ThrowIfCancellationRequested();
        QueueInput([Key(key,0,false),Key(key,2,false)],ref queued);
    }
    static void QueueInput(Input[] inputs,ref int queued)
    {
        var accepted=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>());
        queued+=checked((int)accepted);
        if(accepted==inputs.Length)return;
        // A partial paste burst may leave our synthetic Ctrl down. Release only that modifier.
        if(inputs.Length==4&&inputs[0].Key.Key==0x11&&accepted>0&&accepted<4)
            SendInput(1,[Key(0x11,2,false)],Marshal.SizeOf<Input>());
        throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows rejected text delivery.");
    }

    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
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
