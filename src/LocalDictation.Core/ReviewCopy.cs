using System.Runtime.InteropServices;

namespace LocalDictation.Core;

public enum ReviewCopySource {Reviewed,Dictation,Verification}
public sealed record ReviewCopyResult(bool Success,string Message);

public static class ReviewCopy
{
    public static ReviewCopyResult Copy(ReviewDocument document,ReviewCopySource source,Action<string> writer)
    {
        if(source==ReviewCopySource.Reviewed&&document.UnresolvedCount>0)
            return new(false,"Resolve the differences or keep the remaining dictation first.");
        if(source==ReviewCopySource.Verification&&!document.HasCompletedVerification)
            return new(false,"This item has no confirmed completed verification.");
        var text=source switch
        {
            ReviewCopySource.Dictation=>document.Primary,
            ReviewCopySource.Verification=>document.Verification,
            _=>document.OutputText
        };
        if(text.Length==0)return new(false,"There is no text to copy.");
        try
        {
            writer(text);
            return new(true,source==ReviewCopySource.Reviewed
                ?"Copied. This item remains in Review until you dismiss it."
                :"Original copied. Your phrase choices and final draft are unchanged.");
        }
        catch(ExternalException)
        {
            return new(false,"Clipboard is busy. Try Copy again, or select the text and press Ctrl+C. Your text is preserved.");
        }
    }
}
