using LocalDictation.Core;
using Xunit;

public class PendingReviewTests
{
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
