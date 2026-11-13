using LocalDictation.Core;
using Xunit;

public sealed class SavedVerificationTests
{
    static ReviewDocument Source()
    {
        var source=new ReviewDocument{PrimaryModel="first",Language="en",RequestedLanguage="auto",LanguageMode="detected"};
        source.SetPrimary("original words");
        source.AttachProvenance(new("session",new("first","source-revision"),null,DateTimeOffset.UtcNow));
        return source;
    }
    static SavedVerificationResult Result()=>new("original words","second words",[new("0123456789abcdef","original words","second words",true)],
        "first","source-revision","second","second-revision","comparison","en","forced",159);
    [Fact] public void SecondPassPreservesOriginalAndAuthoritativeManualEdits()
    {
        var source=Source();source.ManualFinalDraft="my correction";source.InsertedAt=DateTimeOffset.UtcNow;
        var copy=SavedVerification.Begin(source,"second");SavedVerification.Complete(copy,source,Result());
        Assert.Equal("original words",copy.Primary);Assert.Equal("second words",copy.Verification);
        Assert.Equal("my correction",copy.OutputText);Assert.Equal("my correction",source.OutputText);
        Assert.Equal("original words",source.Verification);Assert.False(source.VerificationCompleted);
        Assert.Null(copy.InsertedAt);Assert.Equal(source.Id,copy.RetranscriptionOf);
        Assert.Equal("source-revision",copy.Provenance!.Primary.Revision);Assert.Equal("second-revision",copy.Provenance.Verification!.Revision);
        Assert.True(copy.HasCompletedVerification);Assert.Equal(1,copy.UnresolvedCount);
    }
    [Fact] public void EarlierPhraseChoicesRemainAuthoritativeInTheNewComparison()
    {
        var source=Source();source.Pieces=[new("old","original words","chosen words",true)];source.Choices["old"]=new("verification");
        var copy=SavedVerification.Begin(source,"second");
        Assert.Equal("chosen words",copy.ManualFinalDraft);Assert.Equal("original words",copy.Primary);Assert.Single(source.Choices);
    }
    [Fact] public void MalformedComparisonCannotReplaceOrMarkTheDraftVerified()
    {
        var source=Source();var copy=SavedVerification.Begin(source,"second");
        Assert.Throws<InvalidDataException>(()=>SavedVerification.Complete(copy,source,Result() with{Primary="different source"}));
        Assert.Throws<InvalidDataException>(()=>SavedVerification.Complete(copy,source,Result() with{Pieces=[new("bad","original words","second words",true)]}));
        Assert.False(copy.VerificationCompleted);Assert.Equal("original words",copy.Primary);Assert.Empty(copy.Verification);
    }
    [Fact] public void CancelledOrFailedChildKeepsTheKnownRecordingProvenanceForAnotherPass()
    {
        var source=Source();var stopped=SavedVerification.Begin(source,"second");
        var retry=SavedVerification.Begin(stopped,"third");
        Assert.Equal("session",stopped.Provenance!.SessionId);Assert.Equal("source-revision",stopped.Provenance.Primary.Revision);
        Assert.Equal("session",retry.Provenance!.SessionId);Assert.Equal("source-revision",retry.Provenance.Primary.Revision);
        Assert.Null(stopped.Provenance.Verification!.Revision);Assert.False(stopped.VerificationCompleted);
    }
    [Fact] public void CompletingPendingVerifierCannotRewriteKnownSourceIdentity()
    {
        var source=Source();var child=SavedVerification.Begin(source,"second");
        var forged=child.Provenance! with{Primary=new("first","other-source-revision"),Verification=new("second","actual")};
        Assert.Throws<InvalidOperationException>(()=>child.CompletePendingVerificationProvenance(forged));
        Assert.Equal("source-revision",child.Provenance!.Primary.Revision);
        SavedVerification.Complete(child,source,Result());
        Assert.Throws<InvalidOperationException>(()=>child.CompletePendingVerificationProvenance(child.Provenance! with{Verification=new("second","different")}));
    }
    [Fact] public void SameModelIsRejectedAndAgreementStillRequiresExplicitPlacement()
    {
        var source=Source();Assert.Throws<ArgumentException>(()=>SavedVerification.Begin(source,"first"));
        var copy=SavedVerification.Begin(source,"second");
        var agreement=Result() with{Verification="original words",Pieces=[new("0123456789abcdef","original words","original words",false)]};
        SavedVerification.Complete(copy,source,agreement);
        Assert.True(copy.CanInsert);Assert.Null(copy.InsertedAt);Assert.Equal(0,copy.UnresolvedCount);
    }
}
