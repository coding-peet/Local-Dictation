using LocalDictation.Core;
using Xunit;

public class SelectedCaretInsertTests
{
    static TargetGuard ExistingDraft()
    {
        var previous = new TargetGuard("field", "before  after", 7);
        previous.Applied("draft", "before draft after", 12);
        return previous;
    }

    [Fact]
    public void FreshCaptureAtTheOwnedEndReplacesTheDraftAndPreservesSurroundings()
    {
        var previous = ExistingDraft();
        var fresh = new TargetGuard("field", "before draft after", 12);

        Assert.True(fresh.TryAdoptOwnedRangeFrom(previous));
        Assert.True(fresh.HasApplied);
        Assert.Equal("draft", fresh.OwnedText);
        Assert.Equal(7, fresh.Start);
        Assert.Equal(12, fresh.Caret);
        Assert.Equal("before reviewed after", fresh.ReplacedText("reviewed"));
        Assert.Equal("before draft after", previous.ExpectedText);
        Assert.Equal("draft", previous.OwnedText);
    }

    [Theory]
    [InlineData("other-field", "before draft after", 12)]
    [InlineData("field", "Before draft after", 12)]
    [InlineData("field", "before draft AFTER", 12)]
    [InlineData("field", "before draft after", 9)]
    [InlineData("field", "before draft after", 18)]
    public void ANewFieldEditedDocumentOrMovedCaretRemainsANewInsertion(string identity, string text, int caret)
    {
        var fresh = new TargetGuard(identity, text, caret);

        Assert.False(fresh.TryAdoptOwnedRangeFrom(ExistingDraft()));
        Assert.False(fresh.HasApplied);
        Assert.Equal("", fresh.OwnedText);
        Assert.Equal(caret, fresh.Start);
        Assert.Equal(text[..caret] + "reviewed" + text[caret..], fresh.ReplacedText("reviewed"));
    }

    [Fact]
    public void AnUnwrittenOrWithdrawnPreviousDraftCannotBeAdopted()
    {
        var fresh = new TargetGuard("field", "before  after", 7);
        var previous = new TargetGuard("field", "before  after", 7);
        Assert.False(fresh.TryAdoptOwnedRangeFrom(previous));

        previous.Applied("", "before  after", 7);
        Assert.False(fresh.TryAdoptOwnedRangeFrom(previous));
        Assert.False(fresh.HasApplied);
    }

    [Fact]
    public void InvalidOrAlreadyWrittenLeasesCannotAdoptOwnership()
    {
        var previous = ExistingDraft();
        var invalidFresh = new TargetGuard("field", "before draft after", 12);
        invalidFresh.Invalidate();
        Assert.False(invalidFresh.TryAdoptOwnedRangeFrom(previous));

        var writtenFresh = new TargetGuard("field", "before draft after", 12);
        writtenFresh.Applied("", "before draft after", 12);
        Assert.False(writtenFresh.TryAdoptOwnedRangeFrom(previous));

        previous.Invalidate();
        Assert.False(new TargetGuard("field", "before draft after", 12).TryAdoptOwnedRangeFrom(previous));
    }

    [Theory]
    [InlineData("draft", "before WRONG after", 12)]
    [InlineData("draft", "before draft after", 11)]
    [InlineData("draft", "short", 12)]
    [InlineData("much longer than the document", "before draft after", 12)]
    public void IncoherentPreviousOwnershipCannotAuthorizeReplacement(string owned, string text, int caret)
    {
        var previous = new TargetGuard("field", "before  after", 7);
        previous.Applied(owned, text, caret);
        var fresh = new TargetGuard("field", text, caret);

        Assert.False(fresh.TryAdoptOwnedRangeFrom(previous));
        Assert.False(fresh.HasApplied);
    }

    [Fact]
    public void AdoptionRetiresInitialProjectionAndPreservesStrictResumeChecks()
    {
        var fresh = new TargetGuard("field", "before draft after", 12);
        Assert.True(fresh.TryAdoptOwnedRangeFrom(ExistingDraft()));

        Assert.False(fresh.CanUseInitialEmptyProjection("before draft after", "before draft after", 0));
        Assert.True(fresh.CanResume("field", "before draft after"));
        Assert.False(fresh.CanResume("other-field", "before draft after"));
        Assert.False(fresh.CanResume("field", "before edited after"));
        Assert.False(fresh.CanReplace("field", "before draft after", 9, 9));
        Assert.True(fresh.CanReplace("field", "before draft after", 12, 12));
    }

    [Theory]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    public void OnlyAnEnabledVisibleInsertHitInAnInactiveWindowPreservesExternalFocus(
        bool windowActive, bool enabled, bool visible, bool pointerOverInsert, bool expected)
    {
        var result = SelectedCaretActivationPolicy.ShouldPreserveExternalFocus(
            windowActive, enabled, visible, pointerOverInsert);
        Assert.Equal(expected, result);
    }
}
