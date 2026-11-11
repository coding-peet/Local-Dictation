using LocalDictation.Core;
using System.Text.Json;
using Xunit;

public class PendingReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameIdHistoryCopyKeepsItsCurrentChoicesAndInsertionReceipt(bool blockSave)
    {
        var root=Path.Combine(Path.GetTempPath(),"ld-pending-copy-"+Guid.NewGuid());
        var directory=Path.Combine(root,"reviews");
        Directory.CreateDirectory(root);
        try
        {
            var store=new ReviewStore(directory);
            var pending=new PendingReviews(store);
            var original=new ReviewDocument
            {
                Primary="dictation",Verification="reviewed",
                Pieces=[new("difference","dictation","reviewed",true)]
            };
            Assert.True(pending.Save(original));
            // History opens a separate deserialized document for the same recording.
            var updated=JsonSerializer.Deserialize<ReviewDocument>(JsonSerializer.Serialize(original))!;
            updated.Choose("difference","verification");
            updated.InsertedAt=DateTimeOffset.UtcNow;
            if(blockSave)
            {
                Directory.Move(directory,Path.Combine(root,"preserved"));
                File.WriteAllText(directory,"Prevent another directory write.");
            }

            Assert.Equal(!blockSave,pending.Save(updated));
            var current=Assert.Single(pending.Documents);
            Assert.Same(updated,current);
            Assert.Equal("reviewed",current.OutputText);
            Assert.Equal(updated.InsertedAt,current.InsertedAt);
            Assert.False(current.CanInsert);
            Assert.Equal(blockSave,pending.UnsavedIds.Contains(updated.Id));
            if(!blockSave)
            {
                var reopened=Assert.Single(store.Load());
                Assert.Equal("reviewed",reopened.OutputText);
                Assert.Equal(updated.InsertedAt,reopened.InsertedAt);
            }
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact] public void FailedDiskSaveKeepsReviewInMemoryAndDoesNotClaimDurability()
    {
        var root=Path.Combine(Path.GetTempPath(),"ld-pending-"+Guid.NewGuid());
        Directory.CreateDirectory(root);
        var notDirectory=Path.Combine(root,"blocked");
        File.WriteAllText(notDirectory,"file");
        try
        {
            var pending=new PendingReviews(new ReviewStore(notDirectory));
            var review=new ReviewDocument();
            review.SetPrimary("Text that must survive a failed save.");
            Assert.False(pending.Save(review));
            Assert.Same(review,Assert.Single(pending.Documents));
            Assert.Contains(review.Id,pending.UnsavedIds);
        }
        finally{Directory.Delete(root,true);}
    }
}
