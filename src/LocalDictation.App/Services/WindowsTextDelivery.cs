using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

internal enum DeliveryInputMethod { ClipboardPaste,UnicodeKeyboard }

/// <summary>Memory-only receipt for the paste already dispatched; never logged.</summary>
public sealed record PendingPasteConfirmation(string Text,bool IsFinal);

/// <summary>Owns the original draft range. Focus restoration requires explicit Review acceptance.</summary>
public sealed class WindowsTextDelivery : ITextDelivery
{
    readonly nint window;
    readonly TargetGuard guard;
    readonly AutomationElement original;
    readonly TextPatternRange? beforeOriginalCaret;
    readonly string provider;
    readonly ProviderIdentity? capturedProvider;
    readonly string? emptyFieldProjection;
    readonly string? destinationProcessName;
    nint scopeHost;
    DeliveryInputMethod inputMethod=DeliveryInputMethod.ClipboardPaste;
    int invalid,operating,generation,transportFailure;
    readonly object lifecycleGate=new();
    long invalidationVersion;
    PendingNativePaste? pendingPaste;
    readonly ConfirmedReceiptQueue<PendingPasteConfirmation> confirmedReceipts=new();
    sealed record PendingNativePaste(ClipboardTransaction Lease,PasteAcknowledgementSnapshot Paste,
        ProviderIdentity Provider,nint NativeHost,int Generation,long InvalidationVersion,long DispatchedAt,
        DeliveryDiagnostic Diagnostic);
    public nint Window=>window;
    public bool IsConflict=>Volatile.Read(ref invalid)!=0;
    public bool HasTransportFailure=>Volatile.Read(ref transportFailure)!=0;
    public static nint ForegroundWindow=>GetForegroundWindow();
    public string Reason { get; private set; } = "";
    public string InsertedText => guard.OwnedText;
    public DeliveryDiagnostic? LastDiagnostic {get;private set;}

    WindowsTextDelivery(nint window,TargetGuard guard,AutomationElement original,TextPatternRange? beforeOriginalCaret)
        :this(window,guard,original,beforeOriginalCaret,AutomationWindowScope.Resolve(original,window,CancellationToken.None).NativeHost){}
    WindowsTextDelivery(nint window,TargetGuard guard,AutomationElement original,TextPatternRange? beforeOriginalCaret,nint nativeHost)
        :this(window,guard,original,beforeOriginalCaret,nativeHost,null){}
    WindowsTextDelivery(nint window,TargetGuard guard,AutomationElement original,TextPatternRange? beforeOriginalCaret,nint nativeHost,string? emptyFieldProjection)
    {
        this.window=window;this.guard=guard;this.original=original;this.beforeOriginalCaret=beforeOriginalCaret;
        this.emptyFieldProjection=emptyFieldProjection;scopeHost=nativeHost;
        capturedProvider=ReadProviderIdentity(original);provider=ProviderMetadata(capturedProvider);
        destinationProcessName=WindowsDestinationApplication.ProcessName(window);
        if(DiagnosticJournal.Enabled&&capturedProvider is { } shape)
            DiagnosticJournal.Append("capture-metadata.jsonl",new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,
                Stage="provider-shape",shape.FrameworkId,shape.ClassName,shape.ControlTypeId});
    }
    /// <summary>
    /// Reuses a known live draft only when a fresh selected-caret capture proves
    /// the exact same field, complete baseline and owned end. Memory reads only.
    /// The controller serializes adoption with native work and checks its epoch.
    /// </summary>
    public bool TryAdoptOwnedRangeFrom(WindowsTextDelivery previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        // Applied state is monotonic. These checks also prevent reciprocal lock
        // acquisition: only an applied previous lease can feed an unwritten one.
        if (guard.HasApplied || !previous.guard.HasApplied) return false;
        lock (previous.lifecycleGate)
        lock (lifecycleGate)
        {
            if (invalid != 0 || operating != 0 || previous.operating != 0 ||
                transportFailure != 0 || previous.transportFailure != 0 ||
                pendingPaste is not null || previous.pendingPaste is not null ||
                confirmedReceipts.Current is not null || previous.confirmedReceipts.Current is not null ||
                window == 0 || window != previous.window || capturedProvider is null ||
                capturedProvider != previous.capturedProvider)
                return false;

            // A previous lease may have stopped on an old snapshot mismatch.
            // Only the fresh capture's exact current proof can authorize adoption;
            // this never revives that lease or relaxes its automatic-delivery guard.
            return guard.TryAdoptOwnedRangeFrom(previous.guard);
        }
    }

    public void Invalidate()
    {
        lock(lifecycleGate){invalidationVersion++;Interlocked.Exchange(ref invalid,1);pendingPaste=null;}
    }
    public void Suspend()
    {
        lock(lifecycleGate){Interlocked.Increment(ref generation);pendingPaste=null;}
    }
    public bool OwnsProgrammaticFocus(nint root)=>root==window&&Volatile.Read(ref operating)>0;

    // Proven insertion facts survive Suspend/Invalidate and caller timeouts. Only
    // consumption of this exact instance may remove the queue's current receipt.
    public PendingPasteConfirmation? ConfirmedPendingPaste=>confirmedReceipts.Current;
    public void ClearConfirmedPendingPaste(PendingPasteConfirmation sameReceipt)=>confirmedReceipts.ClearSame(sameReceipt);

    public bool HasPendingAcknowledgement
    {
        get
        {
            var pending=CurrentPending(CancellationToken.None);
            return pending is not null;
        }
    }
    PendingNativePaste? CurrentPending(CancellationToken token)
    {
        lock(lifecycleGate)
        {
            if(pendingPaste is not { } pending)return null;
            if(!PendingEligible(pending,token)){pendingPaste=null;return null;}
            return pending;
        }
    }
    bool PendingEligible(PendingNativePaste pending,CancellationToken token) =>
        DeferredPasteConfirmation.CanProbe(new(true,4,Stopwatch.GetElapsedTime(pending.DispatchedAt),
            generation,pending.Generation,invalidationVersion,pending.InvalidationVersion,true,true,true,token.IsCancellationRequested));
    void EnsurePending(PendingNativePaste pending,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock(lifecycleGate)
            if(!ReferenceEquals(pendingPaste,pending)||!PendingEligible(pending,token))throw new Interrupted(InterruptionStage.Invalidated);
    }
    void DropPending(PendingNativePaste pending)
    {
        lock(lifecycleGate)if(ReferenceEquals(pendingPaste,pending))pendingPaste=null;
    }
    T PendingRead<T>(PendingNativePaste pending,CancellationToken token,Func<T> read)
    {
        EnsurePending(pending,token);var value=read();EnsurePending(pending,token);return value;
    }
    string PendingIdentity(PendingNativePaste pending,CancellationToken token,AutomationElement element)
    {
        var pid=PendingRead(pending,token,()=>element.Current.ProcessId);
        var runtime=PendingRead(pending,token,element.GetRuntimeId);
        return pid+":"+string.Join(".",runtime);
    }
    ProviderIdentity PendingProvider(PendingNativePaste pending,CancellationToken token,AutomationElement element) => new(
        PendingRead(pending,token,()=>element.Current.FrameworkId),
        PendingRead(pending,token,()=>element.Current.ClassName),
        PendingRead(pending,token,()=>element.Current.ControlType.Id));
    public PendingPasteConfirmation? TryConfirmPendingPaste(CancellationToken token)
    {
        var pending=CurrentPending(token);
        if(pending is null)return null;
        PendingPasteConfirmation? receipt=null;
        try
        {
            // Readback proves the exact dispatched attempt, independently of the
            // clipboard which has already been returned to its previous contents.
            EnsurePending(pending,token);
            if(!IsWindow(window)||GetForegroundWindow()!=window||!AutomationWindowScope.IsCurrent(pending.NativeHost,window))
                throw new Interrupted(InterruptionStage.ForegroundWindow);
            var focused=PendingRead(pending,token,()=>AutomationElement.FocusedElement);
            if(focused is null||PendingIdentity(pending,token,focused)!=pending.Paste.Identity||
                PendingIdentity(pending,token,original)!=pending.Paste.Identity)
                throw new Interrupted(InterruptionStage.FocusedField);
            EnsurePending(pending,token);
            if(PendingProvider(pending,token,focused)!=pending.Provider)throw new Interrupted(InterruptionStage.FocusedField);
            EnsurePending(pending,token);
            var after=InspectOwned(focused,_=>EnsurePending(pending,token));
            EnsurePending(pending,token);
            if(GetForegroundWindow()!=window||!AutomationWindowScope.IsCurrent(pending.NativeHost,window))
                throw new Interrupted(InterruptionStage.ForegroundWindow);
            var finalFocused=PendingRead(pending,token,()=>AutomationElement.FocusedElement);
            if(finalFocused is null||PendingIdentity(pending,token,finalFocused)!=pending.Paste.Identity||
                PendingProvider(pending,token,finalFocused)!=pending.Provider)throw new Interrupted(InterruptionStage.FocusedField);
            EnsurePending(pending,token);
            var kind=PasteAcknowledgementKind.None;
            lock(lifecycleGate)
            {
                if(!ReferenceEquals(pendingPaste,pending)||!PendingEligible(pending,token))return null;
                kind=DeferredPasteConfirmation.TryAcknowledge(guard,pending.Paste,pending.Paste.Identity,after.Text,after.Caret);
                if(kind==PasteAcknowledgementKind.None)return null;
                receipt=new(pending.Paste.NextText,pending.Paste.IsFinal);
                // Publish before clearing the pending attempt. Caller timeouts or
                // a fresh explicit Insert must still observe this proven receipt.
                confirmedReceipts.Publish(receipt);
                pendingPaste=null;Interlocked.Exchange(ref invalid,0);Interlocked.Exchange(ref transportFailure,0);
            }
            pending.Lease.Acknowledge();
            Reason="";
            LastDiagnostic=pending.Diagnostic with
            {
                Phase="deferred confirm "+kind,Milliseconds=Stopwatch.GetElapsedTime(pending.DispatchedAt).TotalMilliseconds,
                ObservedLength=after.Text.Length,ObservedCaret=after.Caret,
                ExactText=after.Text==pending.Paste.ExpectedText,ExactCaret=after.Caret==pending.Paste.Start+pending.Paste.NextText.Length,
                Unchanged=after.Text==pending.Paste.BeforeText,OutlookParagraphProjection=kind==PasteAcknowledgementKind.OutlookParagraphProjection,
                ClipboardRestored=pending.Lease.WasRestored,ClipboardSuperseded=pending.Lease.WasSuperseded,
                ClipboardRecoveryPending=pending.Lease.RecoveryPending,Conflict=IsConflict,FailureType=null,FailureCode=null,
                TextMismatch=DiagnosticJournal.Enabled?DeliveryDiagnostic.DescribeTextMismatch(pending.Paste.ExpectedText,after.Text,
                    pending.Paste.Start+pending.Paste.NextText.Length):null
            };
            LastDiagnostic.WriteIfEnabled();
            return receipt;
        }
        catch(Exception error) when(error is Interrupted or OperationCanceledException or ElementNotAvailableException or
            InvalidOperationException or COMException or Win32Exception)
        {
            if(error is Interrupted or OperationCanceledException or ElementNotAvailableException)DropPending(pending);
            LastDiagnostic=pending.Diagnostic with{Phase=receipt is null?"deferred readback unavailable":"deferred confirmed recovery pending",
                FailureType=error.GetType().Name,FailureCode=error.HResult,ClipboardRecoveryPending=pending.Lease.RecoveryPending};
            LastDiagnostic.WriteIfEnabled();return receipt;
        }
    }

    public bool IsOriginalFieldCurrent(CancellationToken token)
    {
        var version=Volatile.Read(ref generation);
        try
        {
            CheckLease(token,version);
            if(!IsWindow(window)||GetForegroundWindow()!=window||Identity(original)!=guard.Identity)return false;
            var focused=AutomationElement.FocusedElement;
            if(capturedProvider is null||Identity(focused)!=guard.Identity||ReadProviderIdentity(focused!)!=capturedProvider)return false;
            var scope=AutomationWindowScope.Resolve(focused!,window,token);
            if(scope.Status!=AutomationWindowScope.ScopeStatus.Matched||!AutomationWindowScope.IsCurrent(scope.NativeHost,window))return false;
            var current=InspectOwned(focused);
            CheckLease(token,version);
            return GetForegroundWindow()==window&&Identity(AutomationElement.FocusedElement)==guard.Identity&&
                ReadProviderIdentity(focused!)==capturedProvider&&AutomationWindowScope.IsCurrent(scope.NativeHost,window)&&
                guard.CanResume(Identity(focused),current.Text);
        }
        catch(Exception error) when(error is Interrupted or OperationCanceledException or InvalidOperationException or
            ElementNotAvailableException or COMException or Win32Exception){return false;}
    }

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

    static bool ConfirmedEmptyValue(AutomationElement element,Action<string>? observeStage=null)
    {
        if(element.Current.ControlType!=ControlType.Edit)return false;
        observeStage?.Invoke("empty-value-check");
        var empty=element.TryGetCurrentPattern(ValuePattern.Pattern,out var value)&&
            value is ValuePattern editable&&!editable.Current.IsReadOnly&&editable.Current.Value=="";
        if(empty)observeStage?.Invoke("empty-value-confirmed");
        return empty;
    }

    (string Text,int Caret,TextPattern Pattern,TextPatternRange Selection) InspectOwned(AutomationElement? element,Action<string>? observeStage=null)
    {
        var info=Inspect(element,observeStage);
        // Only this lease's initial, never-written projection may stand for an empty
        // editable value. A stale empty ValuePattern must never hide other text,
        // acknowledge a failed withdrawal or adopt a changed document.
        return guard.CanUseInitialEmptyProjection(emptyFieldProjection,info.Text,info.Caret)&&ConfirmedEmptyValue(element!,observeStage)
            ?("",0,info.Pattern,info.Selection):info;
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
                "empty-value-check"=>CaptureStage.EmptyValueCheck,"empty-value-confirmed"=>CaptureStage.EmptyValueConfirmed,
                _=>CaptureStage.Unknown
            }));
            string? emptyProjection=null;
            if(info.Caret==0&&info.Text.Length>0&&ConfirmedEmptyValue(element,stage=>diagnostic.Stage(
                stage=="empty-value-confirmed"?CaptureStage.EmptyValueConfirmed:CaptureStage.EmptyValueCheck)))
                emptyProjection=info.Text;
            diagnostic.Stage(CaptureStage.Cancellation);
            token.ThrowIfCancellationRequested();
            diagnostic.Stage(CaptureStage.CaretAnchor);
            var beforeCaret=info.Caret==0?null:CaptureBeforeCaret(info.Pattern,info.Selection);
            token.ThrowIfCancellationRequested();
            diagnostic.Stage(CaptureStage.WindowScope);
            var scope=AutomationWindowScope.Resolve(element,window,token);
            diagnostic.Scope(window,scope);
            if(scope.Status!=AutomationWindowScope.ScopeStatus.Matched)
                throw new InvalidOperationException("The selected field could not be linked to the active window. Click the destination and try again.");
            diagnostic.Stage(CaptureStage.PostcheckWindow);
            if (window == 0 || GetForegroundWindow() != window)
                throw new InvalidOperationException("The destination changed during the cursor check.");
            diagnostic.Stage(CaptureStage.PostcheckFocusIdentity);
            if(Identity(AutomationElement.FocusedElement) != Identity(element))
                throw new InvalidOperationException("The destination changed during the cursor check.");
            if(info.Text.Length>0)EmptyFieldDiagnostic.Observe(element,info.Text,info.Caret);
            diagnostic.Stage(CaptureStage.ConstructIdentity);
            var guard=new TargetGuard(Identity(element),emptyProjection==null?info.Text:"",info.Caret);
            diagnostic.Stage(CaptureStage.ProviderMetadata);
            var delivery=new WindowsTextDelivery(window,guard,element,beforeCaret,scope.NativeHost,emptyProjection);
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

    enum InterruptionStage { Invalidated,InputGeneration,ForegroundWindow,FocusedField,DeliveryKeysDown,WindowScope }
    sealed class Interrupted(InterruptionStage stage) : Exception
    {
        public nint? ObservedForegroundWindow {get;private set;}
        public string? ObservedIdentity {get;private set;}
        public Interrupted AttachFocusObservation(nint observedForegroundWindow,string? observedIdentity)
        {
            // Attach only values already read by the existing guard; never query focus for diagnostics.
            if(DiagnosticJournal.Enabled)
            {ObservedForegroundWindow=observedForegroundWindow;ObservedIdentity=observedIdentity;}
            return this;
        }
        public string Stage=>stage switch
        {
            InterruptionStage.Invalidated=>"invalidated",InterruptionStage.InputGeneration=>"input-generation",
            InterruptionStage.ForegroundWindow=>"foreground-window",InterruptionStage.FocusedField=>"focused-field",
            InterruptionStage.DeliveryKeysDown=>"delivery-keys-down",InterruptionStage.WindowScope=>"window-scope",_=>"unknown"
        };
    }
    public bool Apply(string next,CancellationToken token,bool restoreFocus=false) =>
        Apply(next,token,restoreFocus?DeliveryIntent.ExplicitFinal:DeliveryIntent.Live);

    public bool Apply(string next,CancellationToken token,DeliveryIntent intent)
    {
        var restoreFocus=intent.RestoreFocus;
        if(IsConflict)return false;
        Reason="";
        if(next.Any(char.IsControl))
            return Pause("Remove line breaks or control characters from the final draft before inserting. Your original field and Review are unchanged.");
        if(inputMethod==DeliveryInputMethod.UnicodeKeyboard)
        {
            try{UnicodeTypingPlan.Create(next);}
            catch(ArgumentException){return Pause("Remove paragraph separators or invalid Unicode from the final draft before inserting. Your Review is preserved.");}
        }
        int version;long invalidation;
        lock(lifecycleGate){version=generation;invalidation=invalidationVersion;}
        var queued=0;var inputChunks=0;
        var watch=Stopwatch.StartNew();
        var phase="validate";
        var beforeText=guard.ExpectedText;var beforeStart=guard.Start;
        var beforeOwnedText=guard.OwnedText;
        string? expected=null;
        int? observedLength=null,observedCaret=null;
        DeliveryTextMismatch? textMismatch=null;
        DeliveryFocusMismatch? lastFocusMismatch=null;
        bool? textMatches=null,caretMatches=null,unchanged=null,lineEndingsEquivalent=null,terminalParagraphEquivalent=null,nbspEquivalent=null;
        ClipboardTransaction? clipboard=null;
        bool confirmed=false,outlookParagraphProjection=false,selectionMade=false,firstPasteRebased=false;
        bool fullyDispatchedPaste=false,pendingAllowed=false;long dispatchedAt=0;
        FirstPasteEvidence? firstPasteEvidence=null;
        PasteAcknowledgementSnapshot PasteSnapshot() => new(guard.Identity,destinationProcessName,beforeText,beforeStart,
            beforeOwnedText,next,expected!,intent.IsFinal,firstPasteEvidence);
        int focusRestoreChecks=0;bool? providerFocusRequested=null;
        string? failureType=null;int? failureCode=null;
        var readbackRetries=0;
        string? inspectionStage=null,lastReadbackStage=null,lastReadbackFailureType=null;
        string? lastInterruptionStage=null,ownedRangeStrategy=null;
        int? lastReadbackFailureCode=null;
        Action<string> observeInspection=stage=>inspectionStage=stage;
        void RecordInterruption(Interrupted error)
        {
            pendingAllowed=false;lastInterruptionStage=error.Stage;
            if(error.ObservedForegroundWindow is nint observedWindow)
                lastFocusMismatch=DeliveryDiagnostic.DescribeFocusMismatch(guard.Identity,error.ObservedIdentity,window,observedWindow);
        }
        void RecordObservation(string text,int caret)
        {
            observedLength=text.Length;observedCaret=caret;
            textMatches=text==expected;caretMatches=caret==guard.Start+next.Length;unchanged=text==beforeText;
            if(expected==null)return;
            if(DiagnosticJournal.Enabled)textMismatch=DeliveryDiagnostic.DescribeTextMismatch(expected,text,guard.Start+next.Length);
            lineEndingsEquivalent=text.Replace("\r\n","\n")==expected.Replace("\r\n","\n");
            terminalParagraphEquivalent=text.TrimEnd('\r','\n')==expected.TrimEnd('\r','\n');
            nbspEquivalent=text.Replace('\u00a0',' ')==expected.Replace('\u00a0',' ');
        }
        bool AcceptObservation(AutomationElement observed,string text,int caret)
        {
            RecordObservation(text,caret);
            var identity=Identity(observed);
            if(identity!=guard.Identity||capturedProvider is not null&&ReadProviderIdentity(observed)!=capturedProvider)
            {
                pendingAllowed=false;return Stop("The destination changed during insertion.");
            }
            Check(token,version);
            PasteAcknowledgementKind kind;
            lock(lifecycleGate)
            {
                CheckLease(token,version);
                kind=DeferredPasteConfirmation.TryAcknowledge(guard,PasteSnapshot(),identity,text,caret);
                if(kind!=PasteAcknowledgementKind.None&&fullyDispatchedPaste&&clipboard is not null&&intent.IsFinal)
                    // Retain the proven final paste before acknowledgement becomes
                    // visible or finally-restoration can outlive the caller's wait.
                    confirmedReceipts.Publish(new(next,true));
            }
            if(kind==PasteAcknowledgementKind.None)return false;
            outlookParagraphProjection=kind==PasteAcknowledgementKind.OutlookParagraphProjection;
            firstPasteRebased=kind==PasteAcknowledgementKind.FirstPasteBaseline;
            confirmed=true;clipboard?.Acknowledge();return true;
        }
        bool originalWindowActivated=false;
        bool WaitForOriginalFocus(int milliseconds,out Interrupted? lastMismatch)
        {
            var readiness=Stopwatch.StartNew();lastMismatch=null;
            while(true)
            {
                focusRestoreChecks++;CheckLease(token,version);
                if(!AutomationWindowScope.IsCurrent(scopeHost,window))throw new Interrupted(InterruptionStage.WindowScope);
                var foreground=GetForegroundWindow();
                if(foreground!=window)
                {
                    lastMismatch=new Interrupted(InterruptionStage.ForegroundWindow).AttachFocusObservation(foreground,null);
                    // Activation can complete asynchronously. Once our window has
                    // arrived, any subsequent foreground loss stops the operation.
                    if(originalWindowActivated)throw lastMismatch;
                }
                else
                {
                    originalWindowActivated=true;
                    try
                    {
                        var identity=Identity(AutomationElement.FocusedElement);
                        if(identity==guard.Identity)return true;
                        lastMismatch=new Interrupted(InterruptionStage.FocusedField).AttachFocusObservation(foreground,identity);
                    }
                    catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException)
                    {lastMismatch=null;}
                }
                if(readiness.ElapsedMilliseconds>=milliseconds)return false;
                Thread.Sleep(10);
            }
        }
        bool TypeUnicode(string replacement,int common)
        {
            for(var offset=0;offset<replacement.Length;)
            {
                Check(token,version);
                // A complete Unicode scalar is the largest unacknowledged unit.
                // A receiver may move focus while consuming a native input burst.
                var count=char.IsHighSurrogate(replacement[offset])?2:1;
                var chunk=replacement.Substring(offset,count);
                var strokes=UnicodeTypingPlan.Create(chunk);
                phase="queue Unicode input";inputChunks++;
                QueueInput(strokes.Select(stroke=>Key(stroke.Scan,stroke.KeyUp?2u:0u,true)).ToArray(),ref queued);
                offset+=count;
                var partialText=next[..common]+replacement[..offset];
                var partialExpected=guard.ReplacedText(partialText);
                var partialCaret=guard.Start+partialText.Length;
                var acknowledgement=Stopwatch.StartNew();var acknowledged=false;
                phase="confirm Unicode input";
                while(acknowledgement.Elapsed<TimeSpan.FromSeconds(1))
                {
                    try
                    {
                        inspectionStage="Unicode focus-before-read";Check(token,version);
                        var focused=AutomationElement.FocusedElement;
                        var after=InspectOwned(focused,observeInspection);
                        Check(token,version);
                        if(Identity(focused)!=guard.Identity)return Stop("The destination changed during keyboard insertion. Check it before retrying.");
                        RecordObservation(after.Text,after.Caret);
                        if(after.Text==partialExpected&&after.Caret==partialCaret)
                        {
                            acknowledged=true;
                            if(offset==replacement.Length)
                            {
                                confirmed=true;guard.Applied(next,after.Text,after.Caret);return true;
                            }
                            break;
                        }
                    }
                    catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception)
                    {
                        readbackRetries++;lastReadbackStage=inspectionStage;
                        lastReadbackFailureType=error.GetType().Name;lastReadbackFailureCode=error.HResult;
                        Check(token,version);
                    }
                    Thread.Sleep(10);
                }
                if(!acknowledged)return StopTransport("Keyboard insertion was not confirmed. Check the destination; the complete text remains in Review. No retry was sent.",token,version);
                // A later chunk may be queued only after exact progressive text,
                // caret, foreground and original-field acknowledgement above.
            }
            return false;
        }
        bool Reconcile()
        {
            if(expected==null)return false;
            int caret;string actualText;
            try
            {
                Check(token,version);
                if(!TryReadLate(out actualText,out caret))return false;
                Check(token,version);
                if(!AcceptObservation(original,actualText,caret))return false;
            }
            catch(Exception error) when(error is Interrupted or OperationCanceledException or InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception)
            {
                if(error is Interrupted interruption)RecordInterruption(interruption);
                return false;
            }
            phase=firstPasteRebased?"late confirm provider baseline":"late confirm";return true;
        }
        Interlocked.Increment(ref operating);
        try
        {
            phase="window scope";
            if(!IsWindow(window)||Identity(original)!=guard.Identity)
                return Stop("The original destination is no longer available. Choose another destination.");
            var scope=AutomationWindowScope.Resolve(original,window,token);
            CheckLease(token,version);
            if(scope.Status!=AutomationWindowScope.ScopeStatus.Matched||Identity(original)!=guard.Identity)
            {
                scopeHost=0;
                return Pause("The original field could not be linked to its captured window. Choose the destination again.");
            }
            scopeHost=scope.NativeHost;
            if(restoreFocus)
            {
                phase="restore focus";providerFocusRequested=false;
                // Keep held acceptance keys in Review. Moving focus first can
                // redirect their physical repeats to a destination's send action.
                if(!WaitForKeyRelease(DeliveryKeysDown,()=>CheckLease(token,version),()=>Thread.Sleep(10)))
                    return Pause("Release Enter, Space and modifier keys, then try Accept again. The destination was not activated.");
                if(!IsWindow(window)||Identity(original)!=guard.Identity)
                    return Stop("The original destination is no longer available. Choose another destination.");
                if(IsIconic(window))ShowWindow(window,9);
                if(!SetForegroundWindow(window)&&GetForegroundWindow()!=window)
                    return Pause("Windows could not return to the original window. Click that field and try Accept again.");
                // Activation can restore the exact original caret itself. Avoid a
                // redundant provider SetFocus, which can disturb an editable range.
                if(!WaitForOriginalFocus(100,out var mismatch))
                {
                    CheckLease(token,version);
                    if(!AutomationWindowScope.IsCurrent(scopeHost,window))throw new Interrupted(InterruptionStage.WindowScope);
                    providerFocusRequested=true;original.SetFocus();
                    if(!WaitForOriginalFocus(500,out mismatch))
                        throw mismatch??new Interrupted(InterruptionStage.FocusedField);
                }
            }
            phase="validate";
            Check(token,version,false);
            // A modifier-only start/stop chord may still have one key held down.
            if(!WaitForKeyRelease(DeliveryKeysDown,()=>Check(token,version,false),()=>Thread.Sleep(10)))
                return Pause("Release Enter, Space and modifier keys before cursor delivery resumes.");
            Check(token,version);
            var element = AutomationElement.FocusedElement;
            var current = InspectOwned(element,observeInspection);
            if(!guard.CanResume(Identity(element),current.Text))
                return Stop("The original field no longer matches the saved text. Its contents were left untouched; select a destination cursor and click Insert.");
            if(current.Caret!=guard.Caret)
            {
                selectionMade=true;OwnedEnd(current.Pattern).Select();
                Check(token,version);current=InspectOwned(element,observeInspection);
                if(!guard.CanReplace(Identity(element),current.Text,current.Caret,current.Caret))
                    return Pause("The original insertion point could not be restored. Your transcript is preserved.");
            }
            if (next == guard.OwnedText) return true;
            var common = DraftRevision.CommonPrefixBoundary(guard.OwnedText,next);
            var oldTail = guard.OwnedText[common..];
            var replacement = next[common..];
            expected = guard.ReplacedText(next);
            if(expected.Length>100000)return Pause("The completed document would exceed the safe text-check limit. Shorten the draft before inserting.");
            TextPatternRange selected = current.Selection;
            if (oldTail.Length != 0)
            {
                phase="owned range";
                var owned=OwnedSuffix(current.Pattern,current.Selection,oldTail,common,token,version,out ownedRangeStrategy);
                if(owned==null)return StopTransport("The owned draft range is no longer available.",token,version);
                selected=owned;
            }
            if(replacement.Length>0&&inputMethod==DeliveryInputMethod.ClipboardPaste)
            {
                phase="clipboard backup";
                clipboard=ClipboardTransaction.Begin(replacement,()=>Check(token,version));
            }
            Check(token,version);
            // Appending already owns the current caret. Reselecting a collapsed
            // UIA range can invalidate an editor's active selection.
            if(oldTail.Length!=0){selectionMade=true;selected.Select();}
            Check(token,version);
            phase="queue input";
            // Only the mutable suffix is replaced. Stable leading text is untouched.
            if (replacement.Length == 0 && oldTail.Length != 0)
            {
                // Delete selection with a virtual Delete key; never issue Backspace guesses.
                SendKey(0x2E, token,ref queued);
            }
            else if(replacement.Length>0&&inputMethod==DeliveryInputMethod.UnicodeKeyboard)
                return TypeUnicode(replacement,common);
            else if(clipboard!=null)
            {
                phase="clipboard ownership";clipboard.EnsureCurrent();
                // A provider rebase is eligible only after a first paste through the
                // captured collapsed caret. Reread the exact full baseline immediately
                // before dispatch; no empty-field interpretation or selection is made.
                if(!guard.HasApplied&&!selectionMade&&capturedProvider is { } raw)
                {
                    var candidate=new FirstPasteEvidence(destinationProcessName,raw.FrameworkId,raw.ClassName,raw.ControlTypeId,
                        guard.Identity,current.Text,current.Caret,false,true,0);
                    if(candidate.IsKnownProvider)
                    {
                        var pasteElement=AutomationElement.FocusedElement;
                        var pasteBefore=InspectOwned(pasteElement,observeInspection);
                        Check(token,version);
                        if(!guard.CanReplace(Identity(pasteElement),pasteBefore.Text,pasteBefore.Caret,pasteBefore.Caret)||
                            ReadProviderIdentity(pasteElement!)!=capturedProvider)
                            return Stop("The original field changed before insertion. Its contents were left untouched.");
                        firstPasteEvidence=candidate with {Text=pasteBefore.Text,Caret=pasteBefore.Caret};
                    }
                }
                Check(token,version);
                phase="clipboard ownership";
                // One ordinary paste command. The lease returns automatically at
                // its deadline even if the following provider readback stalls.
                // No Enter or application send command is synthesized.
                clipboard.Dispatch(()=>
                {
                    CheckLease(token,version);phase="queue input";
                    QueueInput([Key(0x11,0,false),Key(0x56,0,false),Key(0x56,2,false),Key(0x11,2,false)],ref queued);
                });
                fullyDispatchedPaste=queued==4;pendingAllowed=fullyDispatchedPaste;dispatchedAt=Stopwatch.GetTimestamp();
                // This assignment is unreachable after partial or rejected dispatch.
                if(firstPasteEvidence is { } proof)firstPasteEvidence=proof with {QueuedInputEvents=queued};
            }
            phase="confirm";
            var confirmation=Stopwatch.StartNew();
            while(confirmation.Elapsed<TimeSpan.FromSeconds(1))
            {
                try
                {
                    inspectionStage="focus-before-read";Check(token,version);
                    inspectionStage="focused-element";var afterElement=AutomationElement.FocusedElement;
                    var after=InspectOwned(afterElement,observeInspection);
                    inspectionStage="focus-after-read";Check(token,version);
                    if(Identity(afterElement)!=guard.Identity)return Stop("The destination changed during insertion.");
                    if(AcceptObservation(afterElement!,after.Text,after.Caret))
                    {
                        if(firstPasteRebased)phase="confirm provider baseline";
                        return true;
                    }
                }
                catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception)
                {
                    // A provider can expose an intermediate selection/range while applying paste.
                    // Retry only readback. No new input or target adoption occurs in this loop.
                    readbackRetries++;lastReadbackStage=inspectionStage;
                    lastReadbackFailureType=error.GetType().Name;lastReadbackFailureCode=error.HResult;
                    CheckLease(token,version);
                    var observedForegroundWindow=GetForegroundWindow();
                    if(observedForegroundWindow!=window)throw new Interrupted(InterruptionStage.ForegroundWindow).AttachFocusObservation(observedForegroundWindow,null);
                }
                Thread.Sleep(10);
            }
            if(Reconcile())return true;
            return StopTransport("The destination did not confirm the inserted text. Check the destination before retrying; the complete transcript is preserved in Review.",token,version);
        }
        catch(Interrupted error)
        {
            RecordInterruption(error);failureType=error.GetType().Name;failureCode=error.HResult;
            return queued>0?(expected!=null&&Reconcile()||Stop("Insertion was interrupted. Check the destination before choosing where to insert the preserved Review.")):
            Pause("Cursor delivery is paused. Return to the original unchanged field to resume.");}
        catch(OperationCanceledException)
        {pendingAllowed=false;return queued>0?(expected!=null&&Reconcile()||Stop("Insertion confirmation was interrupted. Check the destination before retrying; the complete transcript is preserved in Review.")):Pause("Cursor validation was cancelled. Your text is preserved.");}
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception)
        {
            failureType=ex.GetType().Name;failureCode=ex.HResult;
            if(phase=="queue input")return expected!=null&&queued>0&&Reconcile()||
                StopTransport("Windows did not confirm text delivery. Check the destination; your complete transcript is preserved.",token,version);
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
            LastDiagnostic=new(phase,intent.IsFinal,queued,watch.Elapsed.TotalMilliseconds,beforeText.Length,next.Length,expected?.Length,
                observedLength,observedCaret,textMatches,caretMatches,unchanged,lineEndingsEquivalent,terminalParagraphEquivalent,nbspEquivalent,
                provider,clipboard?.SavedFormatCount,clipboard?.WasRestored,clipboard?.WasSuperseded,IsConflict,failureType,failureCode,ClipboardRecovery.HasPending,
                readbackRetries,lastReadbackStage,lastReadbackFailureType,lastReadbackFailureCode,lastInterruptionStage,textMismatch,outlookParagraphProjection,lastFocusMismatch,focusRestoreChecks,providerFocusRequested,inputMethod.ToString(),inputChunks,ownedRangeStrategy);
            LastDiagnostic.WriteIfEnabled();
            if(!confirmed&&fullyDispatchedPaste&&pendingAllowed&&expected is not null&&clipboard is not null&&
                capturedProvider is { } raw)
            {
                lock(lifecycleGate)
                {
                    if(version==generation&&invalidation==invalidationVersion&&!token.IsCancellationRequested&&
                        Stopwatch.GetElapsedTime(dispatchedAt)<DeferredPasteConfirmation.MaximumAge)
                        pendingPaste=new(clipboard,PasteSnapshot(),raw,scopeHost,version,invalidation,dispatchedAt,LastDiagnostic);
                }
            }
            Interlocked.Decrement(ref operating);
        }
    }

    bool TryReadLate(out string actualText,out int caret)
    {
        actualText="";caret=0;
        // Read only the captured field. The caller applies the same acknowledgement
        // policy as normal readback, including its payload-length caret after rebase.
        try
        {
            if(!IsWindow(window)||Identity(original)!=guard.Identity)return false;
            var after=InspectOwned(original);
            actualText=after.Text;caret=after.Caret;return true;
        }
        catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or Win32Exception){return false;}
    }
    readonly record struct ProviderIdentity(string FrameworkId,string ClassName,int ControlTypeId);
    static ProviderIdentity? ReadProviderIdentity(AutomationElement element)
    {
        try{return new(element.Current.FrameworkId,element.Current.ClassName,element.Current.ControlType.Id);}
        catch(Exception){return null;}
    }
    static string ProviderMetadata(ProviderIdentity? value)
    {
        if(value is not { } shape)return "unavailable";
        static string Safe(string text)=>new(text.Where(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '.' or '-' or '#').Take(80).ToArray());
        return Safe(shape.FrameworkId)+"/"+Safe(shape.ClassName)+"/"+shape.ControlTypeId;
    }

    static TextPatternRange? CaptureBeforeCaret(TextPattern pattern,TextPatternRange caret)
    {
        try
        {
            var anchor=caret.Clone();
            var moved=anchor.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,-1);
            var document=pattern.DocumentRange;
            return moved==-1&&
                anchor.CompareEndpoints(TextPatternRangeEndpoint.Start,caret,TextPatternRangeEndpoint.Start)<0&&
                anchor.CompareEndpoints(TextPatternRangeEndpoint.End,caret,TextPatternRangeEndpoint.Start)==0&&
                anchor.CompareEndpoints(TextPatternRangeEndpoint.Start,document,TextPatternRangeEndpoint.Start)>=0&&
                anchor.CompareEndpoints(TextPatternRangeEndpoint.End,document,TextPatternRangeEndpoint.End)<=0?anchor:null;
        }
        catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or ArgumentException)
        {return null;}
    }

    TextPatternRange? OwnedSuffix(TextPattern pattern,TextPatternRange caret,string tail,int common,CancellationToken token,int version,out string strategy)
    {
        strategy="unavailable";
        var document=pattern.DocumentRange;
        var expectedPrefix=guard.ExpectedText[..(guard.Start+common)];
        // Anchor strictly before the insertion boundary, whose gravity may move
        // with pasted text. Limit forward probing to a short shared prefix; large
        // unchanged prefixes keep using the shorter caret-to-suffix construction.
        if(common==0||beforeOriginalCaret!=null&&common<128&&common+1<tail.Length)
        {
            TextPatternRange? cached=null;
            Check(token,version);
            try
            {
                var anchored=guard.Start==0&&common==0?document.Clone():beforeOriginalCaret?.Clone();
                if(anchored!=null)
                {
                    var canAdvance=guard.Start==0&&common==0;
                    if(!canAdvance)
                    {
                        var previous=anchored.Clone();
                        var moved=anchored.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,common+1);
                        canAdvance=moved==common+1&&anchored.CompareEndpoints(TextPatternRangeEndpoint.Start,previous,TextPatternRangeEndpoint.Start)>0;
                    }
                    token.ThrowIfCancellationRequested();
                    if(canAdvance)
                    {
                        anchored.MoveEndpointByRange(TextPatternRangeEndpoint.End,caret,TextPatternRangeEndpoint.Start);
                        if(anchored.CompareEndpoints(TextPatternRangeEndpoint.Start,caret,TextPatternRangeEndpoint.Start)<0&&
                            anchored.CompareEndpoints(TextPatternRangeEndpoint.End,caret,TextPatternRangeEndpoint.Start)==0&&
                            anchored.CompareEndpoints(TextPatternRangeEndpoint.Start,document,TextPatternRangeEndpoint.Start)>=0&&
                            anchored.CompareEndpoints(TextPatternRangeEndpoint.End,document,TextPatternRangeEndpoint.End)<=0&&
                            anchored.GetText(tail.Length+1)==tail)
                        {
                            var prefix=document.Clone();
                            prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End,anchored,TextPatternRangeEndpoint.Start);
                            if(prefix.GetText(-1)==expectedPrefix)cached=anchored;
                        }
                    }
                }
            }
            catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or ArgumentException)
            { /* A stale optional anchor is a cache miss, never a new destination. */ }
            Check(token,version);
            if(cached!=null){strategy="cached-anchor";return cached;}
        }
        // Character units are provider-defined, not UTF-16 offsets. Text length
        // only bounds a logarithmic search; every candidate needs exact proofs.
        TextPatternRange? ByCharacterUnits()
        {
            var low=1;var high=tail.Length;var units=high;
            while(low<=high)
            {
                Check(token,version);
                var candidate=caret.Clone();
                var moved=candidate.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,-units);
                if(moved>=0||moved < -units||
                    candidate.CompareEndpoints(TextPatternRangeEndpoint.Start,caret,TextPatternRangeEndpoint.Start)>=0||
                    candidate.CompareEndpoints(TextPatternRangeEndpoint.End,caret,TextPatternRangeEndpoint.Start)!=0||
                    candidate.CompareEndpoints(TextPatternRangeEndpoint.Start,document,TextPatternRangeEndpoint.Start)<0||
                    candidate.CompareEndpoints(TextPatternRangeEndpoint.End,document,TextPatternRangeEndpoint.End)>0)return null;
                var text=candidate.GetText(tail.Length+1);
                if(text.Length==tail.Length)
                {
                    if(text!=tail)return null;
                    var prefix=document.Clone();
                    prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End,candidate,TextPatternRangeEndpoint.Start);
                    Check(token,version);
                    return prefix.GetText(-1)==expectedPrefix?candidate:null;
                }
                if(text.Length>tail.Length)high=units-1;else low=units+1;
                units=low+(high-low)/2;
            }
            return null;
        }
        TextPatternRange? fromCaret;
        try{fromCaret=ByCharacterUnits();}
        catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or ArgumentException)
        {fromCaret=null;}
        Check(token,version);
        if(fromCaret!=null){strategy="caret-units";return fromCaret;}

        // Some editors cannot move a collapsed range by character units. Search
        // backwards only up to the validated caret, then prove the full prefix,
        // exact suffix and both native endpoints before selecting anything.
        try
        {
            var search=document.Clone();
            search.MoveEndpointByRange(TextPatternRangeEndpoint.End,caret,TextPatternRangeEndpoint.Start);
            var found=search.FindText(tail,true,false);
            Check(token,version);
            if(found==null||found.GetText(tail.Length+1)!=tail||
                found.CompareEndpoints(TextPatternRangeEndpoint.Start,caret,TextPatternRangeEndpoint.Start)>=0||
                found.CompareEndpoints(TextPatternRangeEndpoint.End,caret,TextPatternRangeEndpoint.Start)!=0||
                found.CompareEndpoints(TextPatternRangeEndpoint.Start,document,TextPatternRangeEndpoint.Start)<0||
                found.CompareEndpoints(TextPatternRangeEndpoint.End,document,TextPatternRangeEndpoint.End)>0)return null;
            var prefix=document.Clone();
            prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End,found,TextPatternRangeEndpoint.Start);
            Check(token,version);
            if(prefix.GetText(-1)!=expectedPrefix)return null;
            strategy="exact-text";return found;
        }
        catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException or ArgumentException)
        {Check(token,version);return null;}
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
    bool Stop(string message)
    {
        Reason=message;lock(lifecycleGate)Interlocked.Exchange(ref invalid,1);return false;
    }
    bool StopTransport(string message,CancellationToken token,int version)
    {
        // A timeout racing cancellation, user input, lost focus or a vanished target
        // is unresolved delivery, not evidence that the application is incompatible.
        try
        {
            Check(token,version);
            if(!IsWindow(window)||Identity(original)!=guard.Identity||
                capturedProvider is not null&&ReadProviderIdentity(original)!=capturedProvider)return Stop(message);
        }
        catch(Exception error) when(error is Interrupted or OperationCanceledException or InvalidOperationException or
            ElementNotAvailableException or COMException or Win32Exception){return Stop(message);}
        Interlocked.Exchange(ref transportFailure,1);return Stop(message);
    }
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
        if(!AutomationWindowScope.IsCurrent(scopeHost,window))throw new Interrupted(InterruptionStage.WindowScope);
        var observedForegroundWindow=GetForegroundWindow();
        if(observedForegroundWindow!=window)throw new Interrupted(InterruptionStage.ForegroundWindow).AttachFocusObservation(observedForegroundWindow,null);
        var observedIdentity=Identity(AutomationElement.FocusedElement);
        if(observedIdentity!=guard.Identity)throw new Interrupted(InterruptionStage.FocusedField).AttachFocusObservation(observedForegroundWindow,observedIdentity);
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
        if (process == 0) throw IntegrityFailure(pid,"open-process","Cannot validate destination privileges.");
        nint token = 0, data = 0;
        try
        {
            if (!OpenProcessToken(process, 8, out token)) throw IntegrityFailure(pid,"open-token","Cannot validate destination token.");
            GetTokenInformation(token, 25, 0, 0, out var size);
            data = Marshal.AllocHGlobal(size);
            if (!GetTokenInformation(token, 25, data, size, out _)) throw IntegrityFailure(pid,"read-integrity","Cannot validate destination integrity.");
            var sid = Marshal.ReadIntPtr(data);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        finally { if (data != 0) Marshal.FreeHGlobal(data); if (token != 0) CloseHandle(token); CloseHandle(process); }
    }
    static Win32Exception IntegrityFailure(int pid,string operation,string message)
    {
        var error=new Win32Exception(Marshal.GetLastWin32Error(),message);
        error.Data["NativeTargetProcessId"]=pid;error.Data["IntegrityOperation"]=operation;
        return error;
    }
}
