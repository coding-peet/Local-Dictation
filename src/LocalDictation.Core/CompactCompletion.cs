namespace LocalDictation.Core;

public enum CompactCompletionAction {KeepPending,ReviewDifferences,Insert}

public static class CompactCompletion
{
    public static CompactCompletionAction Decide(bool normalCompletion,bool primaryFinal,
        bool verificationRequested,bool verificationCompleted,int unresolved)
    {
        if(!normalCompletion||!primaryFinal||verificationRequested&&!verificationCompleted)
            return CompactCompletionAction.KeepPending;
        return unresolved>0?CompactCompletionAction.ReviewDifferences:CompactCompletionAction.Insert;
    }
}
