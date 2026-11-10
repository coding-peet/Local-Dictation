using System.Runtime.InteropServices;
using System.Text.Json;
using LocalDictation.Core;
using Xunit;

public class CompletionReviewTests
{
    const string Completed="""{"PrimaryModel":"fast","VerificationModel":"verify","Primary":"pôvodný 😀","Verification":"iný text","Pieces":[{"Id":"0123456789abcdef","Primary":"pôvodný 😀","Verification":"iný text","Different":true}],"Choices":{"0123456789abcdef":{"Kind":"primary"}},"ManualFinalDraft":"manual"}""";
    const string Interrupted="""{"PrimaryModel":"fast","VerificationModel":"verify","Primary":"draft","Verification":"draft","Pieces":[{"Id":"primary","Primary":"draft","Verification":"draft","Different":false}]}""";

    [Fact] public void LegacyCompletedAndInterruptedSourcesRemainDistinct()
    {
        var complete=JsonSerializer.Deserialize<ReviewDocument>(Completed)!;
        Assert.True(complete.HasCompletedVerification);
        Assert.Null(complete.Provenance);
        Assert.Equal("manual",complete.OutputText);
        Assert.Single(complete.Choices);
        var interrupted=JsonSerializer.Deserialize<ReviewDocument>(Interrupted)!;
        Assert.False(interrupted.HasCompletedVerification);
        Assert.Null(interrupted.Provenance);
    }

    [Theory]
    [InlineData(ReviewCopySource.Reviewed)]
    [InlineData(ReviewCopySource.Dictation)]
    [InlineData(ReviewCopySource.Verification)]
    public void BusyClipboardCannotEscapeAndCancelAnotherSession(ReviewCopySource source)
    {
        var document=JsonSerializer.Deserialize<ReviewDocument>(Completed)!;
        var before=JsonSerializer.Serialize(document);
        var emergency=false;
        ReviewCopyResult? result=null;
        try{result=ReviewCopy.Copy(document,source,_=>throw new COMException("busy"));}
        catch{emergency=true;}
        Assert.False(emergency);
        Assert.False(result!.Success);
        Assert.Equal(before,JsonSerializer.Serialize(document));
    }

    [Fact] public void OriginalCopyIsExactAndDoesNotChangeTheReviewedDraft()
    {
        var document=JsonSerializer.Deserialize<ReviewDocument>(Completed)!;
        string? copied=null;
        var result=ReviewCopy.Copy(document,ReviewCopySource.Dictation,text=>copied=text);
        Assert.True(result.Success);
        Assert.Equal("pôvodný 😀",copied);
        Assert.Equal("manual",document.OutputText);
    }

    [Fact] public void UnconfirmedVerifierCannotCopyTheLegacyPrimaryPlaceholder()
    {
        var document=JsonSerializer.Deserialize<ReviewDocument>(Interrupted)!;
        var called=false;
        var result=ReviewCopy.Copy(document,ReviewCopySource.Verification,_=>called=true);
        Assert.False(result.Success);
        Assert.False(called);
    }
}
