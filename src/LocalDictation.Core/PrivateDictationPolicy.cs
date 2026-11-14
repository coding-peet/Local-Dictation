namespace LocalDictation.Core;
public static class PrivateDictationPolicy
{
    public static void RequirePersistent(ReviewDocument document)
    {
        if(document.IsPrivate)throw new InvalidOperationException("Private dictation stays in memory and cannot be saved to protected History or Review storage.");
    }
}
