using System.Text.Json;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

/// <summary>Opt-in operational evidence. It never contains text, UI names, paths, audio, or clipboard values.</summary>
public sealed record DeliveryDiagnostic(string Phase,bool Final,int QueuedInputEvents,double Milliseconds,
    int BeforeLength,int RequestedLength,int? ExpectedLength,int? ObservedLength,int? ObservedCaret,
    bool? ExactText,bool? ExactCaret,bool? Unchanged,bool? LineEndingsEquivalent,bool? TerminalParagraphEquivalent,bool? NbspEquivalent,
    string Provider,int? ClipboardFormatCount,bool? ClipboardRestored,bool? ClipboardSuperseded,bool Conflict,string? FailureType=null,int? FailureCode=null,bool ClipboardRecoveryPending=false)
{
    public void WriteIfEnabled()
    {
        if(Environment.GetEnvironmentVariable("LOCAL_DICTATION_DELIVERY_DIAGNOSTICS")!="1")return;
        try
        {
            var directory=Path.Combine(AppPaths.Root,"logs");Directory.CreateDirectory(directory);
            var file=Path.Combine(directory,"delivery-metadata.jsonl");
            if(File.Exists(file)&&new FileInfo(file).Length>256*1024)File.Move(file,file+".previous",true);
            File.AppendAllText(file,JsonSerializer.Serialize(new{Time=DateTimeOffset.UtcNow,Delivery=this})+Environment.NewLine);
        }
        catch(Exception){}
    }
}
