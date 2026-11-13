using LocalDictation.Core;
using Xunit;

public class WholeTranscriptTests
{
    static ReviewDocument Sample()=>new()
    {
        PrimaryModel="first",VerificationModel="alternative",VerificationCompleted=true,
        Primary="Wrong Band 8, Band 9.",Verification="Huawei Band 8.",
        Pieces=[new("difference","Wrong Band 8, Band 9.","Huawei Band 8.",true)],
        ManualFinalDraft="My edited text"
    };
    static void Use(ReviewDocument document,ReviewCopySource source)
    {
        document.UseWholeTranscript(source);
    }
    [Fact]
    public void WholeAlternativeOverridesManualDraftAndPreservesAllOriginals()
    {
        var doc=Sample();Use(doc,ReviewCopySource.Verification);
        Assert.Equal("Huawei Band 8.",doc.OutputText);Assert.Equal(0,doc.UnresolvedCount);
        Assert.Contains("My edited text",doc.ManualDraftArchive);
        Assert.Equal("Wrong Band 8, Band 9.",doc.Primary);Assert.Equal("Huawei Band 8.",doc.Verification);
        Assert.Equal("alternative",doc.VerificationModel);Assert.Null(doc.InsertedAt);
    }
    [Fact]
    public void MissingAlternativeOrAlreadyInsertedReviewCannotBeReplaced()
    {
        var doc=Sample();doc.VerificationCompleted=false;doc.Verification="";
        Assert.Throws<InvalidOperationException>(()=>Use(doc,ReviewCopySource.Verification));
        Assert.Equal("My edited text",doc.OutputText);
        doc=Sample();doc.InsertedAt=DateTimeOffset.UtcNow;
        Assert.Throws<InvalidOperationException>(()=>Use(doc,ReviewCopySource.Verification));
        Assert.Equal("My edited text",doc.OutputText);
    }
    [Fact]
    public void WholeOriginalCanBeSelectedAgainWithoutLosingAlternativeOrEditedDraft()
    {
        var doc=Sample();Use(doc,ReviewCopySource.Verification);Use(doc,ReviewCopySource.Dictation);
        Assert.Equal(doc.Primary,doc.OutputText);Assert.Contains("Huawei Band 8.",doc.ManualDraftArchive);
        Assert.Contains("My edited text",doc.ManualDraftArchive);Assert.Null(doc.InsertedAt);
    }
    [Fact]
    public void SelectedAlternativeSurvivesProtectedSaveAndReload()
    {
        var root=Path.Combine(Path.GetTempPath(),"local-dictation-whole-"+Guid.NewGuid().ToString("N"));
        try
        {
            var doc=Sample();Use(doc,ReviewCopySource.Verification);var store=new ReviewStore(root);store.Save(doc);
            var saved=Assert.Single(store.Load());Assert.Equal(doc.Verification,saved.OutputText);
            Assert.Equal(0,saved.UnresolvedCount);Assert.Contains("My edited text",saved.ManualDraftArchive);Assert.Null(saved.InsertedAt);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
