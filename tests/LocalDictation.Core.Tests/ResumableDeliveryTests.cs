using System.Text.Json;
using LocalDictation.Core;
using Xunit;

public class ResumableDeliveryTests
{
    [Fact] public void ReturningInsideAnUnchangedDraftCanResumeAtItsOwnedEnd()
    {
        var guard=new TargetGuard("field","before  after",7);
        guard.Applied("draft","before draft after",12);
        Assert.False(guard.CanReplace("field","before draft after",9,9));
        Assert.True(guard.CanResume("field","before draft after"));
        Assert.False(guard.CanResume("other","before draft after"));
    }

    [Fact] public void ExternalChangesAndPermanentConflictsCannotResume()
    {
        var guard=new TargetGuard("field","prefix",6);
        Assert.False(guard.CanResume("field","prefix changed"));
        guard.Invalidate();
        Assert.False(guard.CanResume("field","prefix"));
    }

    [Fact] public void ReviewWithdrawalAndApprovedReturnPreserveSurroundingText()
    {
        var guard=new TargetGuard("field","before  after",7);
        guard.Applied("draft","before draft after",12);
        var withdrawn=guard.ReplacedText("");
        Assert.Equal("before  after",withdrawn);
        guard.Applied("",withdrawn,7);
        Assert.True(guard.CanResume("field",withdrawn));
        var approved=guard.ReplacedText("reviewed wording");
        Assert.Equal("before reviewed wording after",approved);
    }

    [Fact] public void InsertionReceiptSurvivesSavingAndPreventsAnotherAccept()
    {
        var doc=new ReviewDocument();doc.SetPrimary("reviewed wording");
        Assert.True(doc.CanInsert);
        doc.InsertedAt=DateTimeOffset.UtcNow;
        var reopened=JsonSerializer.Deserialize<ReviewDocument>(JsonSerializer.Serialize(doc))!;
        Assert.False(reopened.CanInsert);
        Assert.NotNull(reopened.InsertedAt);
    }

    [Fact] public void UnresolvedDifferencesMustBeAcceptedBeforeInsertion()
    {
        var doc=new ReviewDocument {Pieces=[new("choice","a","b",true)]};
        Assert.False(doc.CanInsert);
        doc.KeepRemaining();
        Assert.True(doc.CanInsert);
    }
}
