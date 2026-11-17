using LocalDictation.Core;
using Xunit;

public class DictationSearchTests
{
    static DictationItem Row(string text,bool audio=true,bool pending=false)
    {var d=new ReviewDocument{PrimaryModel="primary",VerificationModel="verifier"};d.SetPrimary(text);return new(d,1,audio,pending,false,text[..Math.Min(80,text.Length)]);}
    [Fact] public void SearchesBeyondPreviewAndAllTermsWithoutChangingText()
    {
        var row=Row(new string('x',90)+" Napatie riadi STM32");row.Document.ManualFinalDraft="Napätie riadi STM32";
        Assert.Same(row,Assert.Single(DictationSearch.Find([row],new("NAPATIE stm32"))));
        Assert.Empty(DictationSearch.Find([row],new("napatie missing")));
        Assert.Equal("Napätie riadi STM32",row.Document.OutputText);
    }
    [Fact] public void RawAndArchivedManualVariantsRemainSearchable()
    {
        var row=Row("original raw");row.Document.Verification="alternative raw";row.Document.ManualFinalDraft="current draft";
        row.Document.PreviousManualDraft="previous wording";row.Document.ManualDraftArchive.Add("archived wording");row.Document.CustomDrafts["part"]="unfinished phrase";
        foreach(var q in new[]{"original","alternative","current","previous","archived","unfinished"})Assert.Single(DictationSearch.Find([row],new(q)));
    }
    [Fact] public void AllFiltersCombineAndModelMatchesEitherRole()
    {
        var match=Row("term",false,true);match.Document.InsertedAt=DateTimeOffset.Now;
        var other=Row("term");
        Assert.Same(match,Assert.Single(DictationSearch.Find([match,other],new("term",Model:"verifier",NeedsReview:true,TextOnly:true,Inserted:true))));
        Assert.Empty(DictationSearch.Find([match],new(Model:"verify")));
    }
    [Fact] public void LocalDateRangeIsInclusiveAndPreservesOrder()
    {
        var early=Row("early");early.Document.Created=new DateTimeOffset(2026,11,14,0,0,0,TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026,11,14)));
        var late=Row("late");late.Document.Created=early.Document.Created.AddHours(23.9);
        var outside=Row("outside");outside.Document.Created=early.Document.Created.AddDays(1);
        Assert.Equal(new[]{late,early},DictationSearch.Find([late,outside,early],new(From:new(2026,11,14),To:new(2026,11,14))));
        Assert.Single(DictationSearch.Find([early],new("2026-11-14")));
    }
    [Fact] public void EmptyQueryIncludesTextOnlyAndRemovedModels()
    {
        var row=Row("saved",false);row.Document.PrimaryModel="removed/model";
        Assert.Single(DictationSearch.Find([row],new()));Assert.Single(DictationSearch.Find([row],new(Model:"removed/model")));
    }
    [Fact] public void ReversedDatesAreRejected()
    {Assert.Throws<ArgumentException>(()=>DictationSearch.Find([],new(From:new(2026,11,15),To:new(2026,11,14))));}
}
