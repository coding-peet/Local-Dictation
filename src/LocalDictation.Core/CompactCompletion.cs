namespace LocalDictation.Core;

public enum AfterRecordingAction {InsertDirectly,PreviewFirst}
public enum CompactCompletionAction {KeepPending,ReviewDifferences,Insert,Preview}

public static class CompactCompletion
{
    public static CompactCompletionAction Decide(bool normalCompletion,bool primaryFinal,
        bool verificationRequested,bool verificationCompleted,int unresolved,
        AfterRecordingAction afterRecording=AfterRecordingAction.InsertDirectly)
    {
        if(!normalCompletion||!primaryFinal||verificationRequested&&!verificationCompleted)
            return CompactCompletionAction.KeepPending;
        if(afterRecording==AfterRecordingAction.PreviewFirst)return CompactCompletionAction.Preview;
        if(afterRecording!=AfterRecordingAction.InsertDirectly)return CompactCompletionAction.KeepPending;
        return unresolved>0?CompactCompletionAction.ReviewDifferences:CompactCompletionAction.Insert;
    }
}
