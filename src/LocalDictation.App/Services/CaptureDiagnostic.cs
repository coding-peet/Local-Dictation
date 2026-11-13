using System.Diagnostics;

namespace LocalDictation.App.Services;

public enum CaptureStage
{
    Foreground,FocusedElement,OwnProcess,Integrity,Availability,TextPattern,ReadOnly,
    SelectionCount,SelectionCollapse,DocumentText,CaretPrefix,PrefixCoherence,
    Cancellation,CaretAnchor,PostcheckWindow,PostcheckFocusIdentity,ConstructIdentity,ProviderMetadata,Unknown,WindowScope,EmptyValueCheck,EmptyValueConfirmed
}

public sealed record CaptureProviderMetadata(string FrameworkId,string ClassName,int ControlTypeId,int TargetProcessId,
    bool HasKeyboardFocus,bool IsKeyboardFocusable,int[] SupportedPatternIds);

/// <summary>Fixed capture stages and opt-in failed-provider shape. Never logs field values or exception messages.</summary>
public sealed class CaptureDiagnostic
{
    readonly Stopwatch? watch;
    CaptureStage? current;
    CaptureDiagnostic(){if(DiagnosticJournal.Enabled)watch=Stopwatch.StartNew();}
    public static CaptureDiagnostic Begin()=>new();

    public void Stage(CaptureStage stage)
    {
        if(watch==null)return;
        if(current.HasValue)Write(Name(current.Value),true);
        current=stage;Write(Name(stage),null);
    }
    public void Complete()
    {
        if(watch==null)return;
        if(current.HasValue)Write(Name(current.Value),true);
        Write("complete",true);
    }
    public void Fail(Exception error)
    {
        if(watch!=null)Write(current.HasValue?Name(current.Value):"begin",false,error);
    }
    internal void Scope(nint expectedWindow,AutomationWindowScope.Scope scope)
    {
        if(watch==null||!DiagnosticJournal.Enabled)return;
        var status=scope.Status switch
        {
            AutomationWindowScope.ScopeStatus.Matched=>"matched",
            AutomationWindowScope.ScopeStatus.Mismatched=>"mismatched",_=>"unresolved"
        };
        DiagnosticJournal.Append("capture-metadata.jsonl",new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,
            Stage="window-scope-proof",Milliseconds=watch.Elapsed.TotalMilliseconds,Status=status,
            ExpectedWindow=expectedWindow.ToInt64(),NativeHost=scope.NativeHost.ToInt64(),Root=scope.Root.ToInt64(),
            Matched=scope.Status==AutomationWindowScope.ScopeStatus.Matched});
    }
    public void FailedProvider(Func<CaptureProviderMetadata> read)
    {
        // The factory can query the provider. Never invoke it in ordinary operation,
        // before access checks pass, or for unrelated capture failures.
        if(watch==null||!DiagnosticJournal.Enabled||current!=CaptureStage.TextPattern)return;
        try
        {
            var source=read();
            static string Safe(string value)=>new((value??"").Where(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '.' or '-' or '#').Take(80).ToArray());
            var shape=source with{FrameworkId=Safe(source.FrameworkId),ClassName=Safe(source.ClassName),
                SupportedPatternIds=(source.SupportedPatternIds??[]).Take(32).ToArray()};
            DiagnosticJournal.Append("capture-metadata.jsonl",new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,
                Stage="failed-provider",Success=false,Milliseconds=watch.Elapsed.TotalMilliseconds,Provider=shape});
        }
        catch(Exception){} // Metadata must never replace the original capture failure.
    }
    void Write(string stage,bool? success,Exception? error=null)=>DiagnosticJournal.Append("capture-metadata.jsonl",
        new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,Stage=stage,Success=success,
            Milliseconds=watch!.Elapsed.TotalMilliseconds,FailureType=error?.GetType().Name,FailureCode=error?.HResult});

    static string Name(CaptureStage stage)=>stage switch
    {
        CaptureStage.Foreground=>"foreground",CaptureStage.FocusedElement=>"focused-element",
        CaptureStage.OwnProcess=>"own-process",CaptureStage.Integrity=>"integrity",
        CaptureStage.Availability=>"availability",CaptureStage.TextPattern=>"text-pattern",
        CaptureStage.ReadOnly=>"read-only",CaptureStage.SelectionCount=>"selection-count",
        CaptureStage.SelectionCollapse=>"selection-collapse",CaptureStage.DocumentText=>"document-text",
        CaptureStage.CaretPrefix=>"caret-prefix",CaptureStage.PrefixCoherence=>"prefix-coherence",
        CaptureStage.EmptyValueCheck=>"empty-value-check",CaptureStage.EmptyValueConfirmed=>"empty-value-confirmed",
        CaptureStage.CaretAnchor=>"caret-anchor",CaptureStage.Cancellation=>"cancellation",CaptureStage.PostcheckWindow=>"postcheck-window",
        CaptureStage.PostcheckFocusIdentity=>"postcheck-focus-identity",CaptureStage.ConstructIdentity=>"construct-identity",
        CaptureStage.ProviderMetadata=>"provider-metadata",CaptureStage.WindowScope=>"window-scope",_=>"unknown"
    };
}
