using System.Reflection;

namespace LocalDictation.App.Services;

/// <summary>Opt-in operational evidence. It never contains text, UI names, paths, audio, or clipboard values.</summary>
public sealed record DeliveryDiagnostic(string Phase,bool Final,int QueuedInputEvents,double Milliseconds,
    int BeforeLength,int RequestedLength,int? ExpectedLength,int? ObservedLength,int? ObservedCaret,
    bool? ExactText,bool? ExactCaret,bool? Unchanged,bool? LineEndingsEquivalent,bool? TerminalParagraphEquivalent,bool? NbspEquivalent,
    string Provider,int? ClipboardFormatCount,bool? ClipboardRestored,bool? ClipboardSuperseded,bool Conflict,string? FailureType=null,int? FailureCode=null,bool ClipboardRecoveryPending=false,
    int ReadbackRetries=0,string? LastReadbackStage=null,string? LastReadbackFailureType=null,int? LastReadbackFailureCode=null,
    string? LastInterruptionStage=null)
{
    public void WriteIfEnabled()
    {
        if(!DiagnosticJournal.Enabled)return;
        DiagnosticJournal.Append("delivery-metadata.jsonl",new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,Delivery=this});
    }

    public static void WriteSessionIfEnabled()
    {
        if(!DiagnosticJournal.Enabled)return;
        try
        {
            var assembly=typeof(DeliveryDiagnostic).Assembly;
            var buildVersion=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??assembly.GetName().Version?.ToString();
            DiagnosticJournal.Session(new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,BuildVersion=buildVersion});
        }
        catch(Exception){}
    }
}
