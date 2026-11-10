using LocalDictation.Core;
using Xunit;

public class ReviewTests
{
    static ReviewDocument Sample() => new()
    {
        Primary = "Ahoj svet 😀.", Verification = "Ahoj, svet 😀!",
        Pieces = [new("a","Ahoj","Ahoj",false),new("b","",",",true),
                  new("c"," svet 😀"," svet 😀",false),new("d",".","!",true)]
    };

    [Fact] public void ChoicesPreserveExactTextAndUnresolvedPrimary()
    {
        var review = Sample();
        Assert.Equal(review.Primary, review.WorkingText);
        Assert.Equal(2, review.UnresolvedCount);
        review.Choose("b", "verification");
        review.Choose("d", "custom", "?");
        Assert.Equal("Ahoj, svet 😀?", review.WorkingText);
        Assert.Equal("Ahoj svet 😀.", review.Primary);
        Assert.Equal(0, review.UnresolvedCount);
        review.Undo();
        Assert.Equal(1, review.UnresolvedCount);
    }

    [Fact] public void BulkKeepPreservesCustomAndManualDraft()
    {
        var review = Sample();
        review.Choose("b","custom"," —");
        review.ManualFinalDraft = "My exact manual text";
        review.KeepRemaining();
        Assert.Equal("My exact manual text",review.OutputText);
        Assert.Equal("Ahoj — svet 😀.",review.WorkingText);
        review.RebuildDraft();
        Assert.Equal("My exact manual text",review.PreviousManualDraft);
        Assert.Equal(review.WorkingText,review.OutputText);
    }

    [Fact] public void ProtectedStoreRecoversPendingAndExplicitDismissDeletes()
    {
        var directory = Path.Combine(Path.GetTempPath(),"local-dictation-tests-"+Guid.NewGuid());
        try
        {
            var store = new ReviewStore(directory);
            var review = Sample();
            review.CustomDrafts["b"] = "unapplied draft";
            review.Choose("d","primary");
            store.Save(review);
            var bytes = File.ReadAllBytes(Path.Combine(directory,review.Id+".review"));
            Assert.DoesNotContain(review.Primary,System.Text.Encoding.UTF8.GetString(bytes));
            var restored = Assert.Single(store.Load());
            Assert.Equal(review.Id,restored.Id);
            Assert.Equal("unapplied draft",restored.CustomDrafts["b"]);
            Assert.Equal(1,restored.UnresolvedCount);
            store.Delete(review.Id);
            Assert.Empty(store.Load());
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }

    [Fact] public void ModifierChordIgnoresOtherKeysAndRepeat()
    {
        var chord = new ModifierChord();
        Assert.False(chord.Update(0xA2,true));
        Assert.False(chord.Update(0xA0,true));
        Assert.False(chord.Update(0xA0,true));
        Assert.True(chord.Update(0xA0,false));
        Assert.False(chord.Update(0xA2,false));
        chord.Update(0xA2,true); chord.Update(0xA0,true); chord.Update(0x53,true);
        Assert.False(chord.Update(0xA0,false));
    }

    [Fact] public void TargetOwnershipRequiresExactFieldSelectionAndRevision()
    {
        var guard = new TargetGuard("field","prefix suffix",7);
        Assert.True(guard.CanReplace("field","prefix suffix",7,7));
        guard.Applied("hello", "prefix hello suffix",12);
        Assert.True(guard.CanReplace("field","prefix hello suffix",12,12));
        Assert.False(guard.CanReplace("field","user edit",12,12));
        Assert.False(guard.CanReplace("other","prefix hello suffix",12,12));
        Assert.False(guard.CanReplace("field","prefix hello suffix",0,0));
    }
}
