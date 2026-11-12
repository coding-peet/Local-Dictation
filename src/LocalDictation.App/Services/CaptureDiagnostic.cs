using System.Diagnostics;

namespace LocalDictation.App.Services;

public enum CaptureStage
{
    Foreground,FocusedElement,OwnProcess,Integrity,Availability,TextPattern,ReadOnly,
    SelectionCount,SelectionCollapse,DocumentText,CaretPrefix,PrefixCoherence,
    Cancellation,PostcheckWindow,PostcheckFocusIdentity,ConstructIdentity,ProviderMetadata,Unknown
}

/// <summary>Fixed capture stages only. Never queries a native element or logs exception messages.</summary>
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
        CaptureStage.Cancellation=>"cancellation",CaptureStage.PostcheckWindow=>"postcheck-window",
        CaptureStage.PostcheckFocusIdentity=>"postcheck-focus-identity",CaptureStage.ConstructIdentity=>"construct-identity",
        CaptureStage.ProviderMetadata=>"provider-metadata",_=>"unknown"
    };
}
