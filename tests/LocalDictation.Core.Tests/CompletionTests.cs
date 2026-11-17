using LocalDictation.Core;
using Xunit;

public class CompletionTests
{
    [Fact] public void StoredProvenanceAndOriginalsSurviveChoicesAndProtectedReload()
    {
        var path=Path.Combine(Path.GetTempPath(),"ld-provenance-"+Guid.NewGuid());
        try
        {
            var d=new ReviewDocument {PrimaryModel="fast",VerificationModel="verify",
                Primary="pôvodný 😀\n",Verification="iný text",
                Pieces=[new("region","pôvodný 😀\n","iný text",true)]};
            d.AttachProvenance(new("session",new("fast","aaa"),new("verify","bbb"),DateTimeOffset.UtcNow,"snapshot"));
            d.Choose("region","verification");d.ManualFinalDraft="manual";
            var store=new ReviewStore(path);store.Save(d);
            var copy=Assert.Single(store.Load());
            Assert.Equal(d.Provenance,copy.Provenance);
            Assert.Equal("pôvodný 😀\n",copy.Primary);
            Assert.Equal("iný text",copy.Verification);
            Assert.Equal("manual",copy.OutputText);
            Assert.Throws<InvalidOperationException>(()=>copy.AttachProvenance(
                d.Provenance! with {Primary=new("fast","different")}));
        }
        finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }

    [Fact] public void OldReviewDoesNotInventARevision()
    {
        var d=System.Text.Json.JsonSerializer.Deserialize<ReviewDocument>("""{"PrimaryModel":"old","Primary":"text"}""")!;
        Assert.Null(d.Provenance);
    }

    [Fact] public void BannerUsesNegativeMonitorWorkArea()
    {
        var bounds=BannerPlacement.Calculate(new(-1920,0,1920,1040),590,110,1);
        Assert.Equal(new PixelBounds(-1255,908,590,110),bounds);
    }

    [Fact] public void BannerScalesAndClampsToWorkArea()
    {
        var bounds=BannerPlacement.Calculate(new(1920,-300,1280,720),590,110,1.5);
        Assert.Equal(new PixelBounds(2117,222,885,165),bounds);
        var small=BannerPlacement.Calculate(new(-400,-200,400,200),590,110,2);
        Assert.True(small.Left>=-400&&small.Right<=0);
        Assert.True(small.Top>=-200&&small.Bottom<=0);
    }    [Fact] public void PrimaryCheckpointDoesNotInventVerificationOutput()
    {
        var d=new ReviewDocument {VerificationModel="verify"};
        d.SetPrimary("primary");
        Assert.Equal("",d.Verification);
        Assert.False(d.VerificationCompleted);
    }

}
