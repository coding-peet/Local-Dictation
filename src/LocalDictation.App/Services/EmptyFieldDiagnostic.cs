using System.Windows.Automation;

namespace LocalDictation.App.Services;

// Opt-in technical evidence from the already validated original field only.
// No field name, text, value, clipboard content or text hash is serialized.
static class EmptyFieldDiagnostic
{
    public static void Observe(AutomationElement element,string documentText,int caret)
    {
        if(!DiagnosticJournal.Enabled)return;
        DiagnosticJournal.Append("capture-metadata.jsonl",new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,
            Stage="empty-field-shape-entry"});
        try
        {
            NativeFieldValues.Shape value=NativeFieldValues.Describe(false,null,null,documentText);
            if(element.TryGetCurrentPattern(ValuePattern.Pattern,out var valueObject)&&valueObject is ValuePattern pattern)
            {
                var data=pattern.Current;
                value=NativeFieldValues.Describe(true,data.IsReadOnly,data.Value,documentText);
            }
            var native=NativeFieldValues.Read(element,documentText);
            var patterns=element.GetSupportedPatterns().Select(pattern=>pattern.Id).Take(32).ToArray();
            DiagnosticJournal.Append("capture-metadata.jsonl",new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,
                Stage="empty-field-shape",DocumentLength=documentText.Length,Caret=caret,
                ManagedValue=value,Native=native,SupportedPatternIds=patterns});
        }
        catch(Exception error)
        {
            DiagnosticJournal.Append("capture-metadata.jsonl",new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,
                Stage="empty-field-shape-failed",FailureType=error.GetType().Name,FailureCode=error.HResult});
        }
    }
}
