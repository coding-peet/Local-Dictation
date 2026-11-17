using LocalDictation.Core;
using Xunit;

public class CompactReviewProgressTests
{
    static ReviewDocument Sample()=>new()
    {
        Primary="Ahoj svet 😀.",Verification="Ahoj, svet 😀!",
        Pieces=[new("start","Ahoj","Ahoj",false),new("comma","",",",true),
            new("middle"," svet 😀"," svet 😀",false),new("end",".","!",true)]
    };

    [Fact] public void StartsAtFirstUnresolvedPhraseAndSkipsPreviouslyReviewedOnes()
    {
        var doc=Sample();doc.Choose("comma","primary");
        var progress=new CompactReviewProgress(doc);
        Assert.Same(doc,progress.Document);
        Assert.Equal("end",progress.SelectedId);
        Assert.False(progress.IsComplete);
    }

    [Fact] public void EachChoiceAdvancesAndLastChoiceCompletesTheReview()
    {
        var doc=Sample();var progress=new CompactReviewProgress(doc);
        Assert.Equal("comma",progress.SelectedId);
        progress.Choose("verification");
        Assert.Equal("end",progress.SelectedId);
        Assert.Equal(1,doc.UnresolvedCount);
        progress.Choose("verification");
        Assert.Null(progress.SelectedId);
        Assert.True(progress.IsComplete);
        Assert.Equal("Ahoj, svet 😀!",doc.WorkingText);
        Assert.Equal("Ahoj svet 😀.",doc.Primary);
    }

    [Fact] public void ChoosingTheLastPhraseWrapsToAnEarlierUnresolvedPhrase()
    {
        var progress=new CompactReviewProgress(Sample());
        progress.Select("end");progress.Choose("primary");
        Assert.Equal("comma",progress.SelectedId);
        Assert.False(progress.IsComplete);
    }

    [Fact] public void AResolvedPhraseCanBeCorrectedWithoutRevisitingOtherResolvedPhrases()
    {
        var doc=Sample();var progress=new CompactReviewProgress(doc);
        progress.Choose("primary");
        progress.Select("comma");progress.Refresh();
        Assert.Equal("comma",progress.SelectedId);
        progress.Choose("verification");
        Assert.Equal("end",progress.SelectedId);
        Assert.Equal(",",doc.WorkingText.Substring(4,1));
    }

    [Fact] public void EmptyAndCustomAlternativesPreserveExactTextAndManualDraft()
    {
        var doc=Sample();doc.ManualFinalDraft="Manual text stays authoritative.\n";
        var progress=new CompactReviewProgress(doc);
        progress.Choose("verification");
        progress.Choose("custom","  č. 42 😀 ");
        Assert.Equal("Ahoj, svet 😀  č. 42 😀 ",doc.WorkingText);
        Assert.Equal("Manual text stays authoritative.\n",doc.OutputText);
        progress.Select("end");progress.Choose("custom","");
        Assert.Equal("Ahoj, svet 😀",doc.WorkingText);
        Assert.True(progress.IsComplete);
    }

    [Fact] public void BulkKeepPreservesExistingChoicesAndUndoCanResumeReview()
    {
        var doc=Sample();var progress=new CompactReviewProgress(doc);
        progress.Choose("verification");progress.KeepRemaining();
        Assert.True(progress.IsComplete);Assert.Null(progress.SelectedId);
        Assert.Equal("Ahoj, svet 😀.",doc.OutputText);
        doc.Undo();progress.Refresh();
        Assert.False(progress.IsComplete);
        Assert.Equal("end",progress.SelectedId);
        Assert.Equal("verification",doc.Choices["comma"].Kind);
    }

    [Fact] public void NoDifferencesNeedNoSelectionAndCannotReceiveAnAccidentalChoice()
    {
        var doc=new ReviewDocument();doc.SetPrimary("An agreed transcript.");
        var progress=new CompactReviewProgress(doc);
        Assert.True(progress.IsComplete);Assert.Null(progress.SelectedId);
        Assert.Throws<InvalidOperationException>(()=>progress.Choose("primary"));
        Assert.Empty(doc.History);
    }

    [Fact] public void InvalidSelectionOrWordingCannotAdvanceOrModifyTheReview()
    {
        var doc=Sample();var progress=new CompactReviewProgress(doc);
        Assert.Throws<ArgumentException>(()=>progress.Select("middle"));
        Assert.Throws<ArgumentException>(()=>progress.Select("missing"));
        Assert.Throws<ArgumentException>(()=>progress.Choose("unknown"));
        Assert.Equal("comma",progress.SelectedId);Assert.Empty(doc.Choices);Assert.Empty(doc.History);
    }

    [Fact] public void RefreshRecoversAfterTheSelectedPieceIsReplaced()
    {
        var doc=Sample();var progress=new CompactReviewProgress(doc);
        doc.Pieces.RemoveAll(p=>p.Id=="comma");progress.Refresh();
        Assert.Equal("end",progress.SelectedId);
    }
}
