using System.Text.Json;
using LocalDictation.Core;
using Xunit;
public class PrivateDictationTests
{
    [Fact] public void PrivateMarkerSurvivesInMemoryCloningAndEveryProtectedStoreRefusesIt()
    {
        var d=new ReviewDocument{IsPrivate=true};d.SetPrimary("private synthetic words");
        var copy=JsonSerializer.Deserialize<ReviewDocument>(JsonSerializer.Serialize(d))!;Assert.True(copy.IsPrivate);
        var path=Path.Combine(Path.GetTempPath(),"private-policy-"+Guid.NewGuid().ToString("N"));
        var store=new ReviewStore(Path.Combine(path,"review"));var history=new HistoryStore(Path.Combine(path,"history"));var pending=new PendingReviews(store);
        Assert.Throws<InvalidOperationException>(()=>store.Save(d));Assert.Throws<InvalidOperationException>(()=>history.Add(d,new byte[32000]));
        Assert.Throws<InvalidOperationException>(()=>history.Update(d));Assert.Throws<InvalidOperationException>(()=>pending.Save(d));
        Assert.Empty(pending.Documents);Assert.False(Directory.Exists(path));
    }
    [Fact] public void OrdinaryReviewStillUsesProtectedStorage()
    {
        var d=new ReviewDocument();d.SetPrimary("ordinary synthetic words");var path=Path.Combine(Path.GetTempPath(),"ordinary-policy-"+Guid.NewGuid().ToString("N"));
        try{var store=new ReviewStore(path);store.Save(d);Assert.Equal(d.Primary,Assert.Single(store.Load()).Primary);}
        finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
