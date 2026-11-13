using LocalDictation.Core;
using Xunit;

public class DictationLibraryTests
{
    static ReviewDocument Doc(string text){var doc=new ReviewDocument();doc.SetPrimary(text);return doc;}
    [Fact] public void PendingTextRemainsVisibleWithoutItsEvictedAudio()
    {
        var doc=Doc("retained text");var row=Assert.Single(DictationLibrary.Merge([], [doc],new HashSet<string>()));
        Assert.Same(doc,row.Document);Assert.False(row.HasAudio);Assert.True(row.NeedsReview);Assert.Equal("Needs review",row.State);Assert.Contains("Text only",row.Label);
    }
    [Fact] public void CurrentPendingEditsReplaceTheHistorySnapshotWithoutDuplicateRows()
    {
        var old=Doc("original");var edited=Doc("original");edited.Id=old.Id;edited.ManualFinalDraft="my edit";
        var row=Assert.Single(DictationLibrary.Merge([new(old,12)], [edited],new HashSet<string>()));
        Assert.Same(edited,row.Document);Assert.True(row.HasAudio);Assert.Equal(12,row.DurationSeconds);Assert.Contains("my edit",row.Label);Assert.True(row.NeedsReview);
    }
    [Fact] public void DurableHistoryReceiptWinsOverStalePendingAndDoesNotMutateInputs()
    {
        var saved=Doc("inserted");saved.InsertedAt=DateTimeOffset.UtcNow;var stale=Doc("old");stale.Id=saved.Id;
        var row=Assert.Single(DictationLibrary.Merge([new(saved,2)], [stale],new HashSet<string>()));
        Assert.Same(saved,row.Document);Assert.Equal("Inserted",row.State);Assert.False(row.NeedsReview);Assert.Null(stale.InsertedAt);
    }
    [Fact] public void NewerPendingEditsRemainVisibleWhenBothStoresHaveAReceipt()
    {
        var history=Doc("old saved wording");history.InsertedAt=DateTimeOffset.UtcNow;
        var current=Doc("new saved wording");current.Id=history.Id;current.InsertedAt=history.InsertedAt;
        var row=Assert.Single(DictationLibrary.Merge([new(history,2)],[current],new HashSet<string>()));
        Assert.Same(current,row.Document);Assert.Equal("new saved wording",row.Document.OutputText);Assert.Equal("Inserted",row.State);
    }
    [Fact] public void UnsavedRecoveryIsExplicitAndSavedRowsDoNotBecomePending()
    {
        var saved=Doc("ordinary");var pending=Doc("unsaved");
        var rows=DictationLibrary.Merge([new(saved,2)],[pending],new HashSet<string>{pending.Id});
        Assert.Equal("Saved",rows.Single(r=>r.Id==saved.Id).State);Assert.Equal("Not saved",rows.Single(r=>r.Id==pending.Id).State);
    }
    [Fact] public void AudioLimitCannotHideAdditionalPendingDocuments()
    {
        var history=Enumerable.Range(0,20).Select(i=>new HistoryEntry(Doc("history"),i+1)).ToList();
        var pending=Enumerable.Range(0,3).Select(i=>Doc("pending")).ToList();
        Assert.Equal(23,DictationLibrary.Merge(history,pending,new HashSet<string>()).Count);
    }
    [Fact] public void NewestCreatedItemIsFirstRegardlessOfWhichStoreItComesFrom()
    {
        var old=Doc("old");old.Created=DateTimeOffset.UtcNow.AddDays(-1);var fresh=Doc("fresh");
        Assert.Equal(fresh.Id,DictationLibrary.Merge([new(old,2)],[fresh],new HashSet<string>())[0].Id);
    }
}
