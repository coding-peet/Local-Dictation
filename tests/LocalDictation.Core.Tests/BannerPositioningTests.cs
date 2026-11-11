using LocalDictation.Core;
using Xunit;

public class BannerPositioningTests
{
    [Fact] public void DraggedPositionIsNotRecenteredByRefresh()
    {
        var requested=new PixelBounds(80,120,614,300);
        Assert.Equal(requested,BannerPositioning.Clamp(new(0,0,1920,1040),requested));
    }

    [Fact] public void DraggedBannerStaysInsideNegativeMonitorAndResizedPreviewFits()
    {
        Assert.Equal(new PixelBounds(-614,-200,614,500),
            BannerPositioning.Clamp(new(-1920,-200,1920,1040),new(-20,-400,614,500)));
        Assert.Equal(new PixelBounds(-400,-200,400,200),
            BannerPositioning.Clamp(new(-400,-200,400,200),new(-900,-600,1200,900)));
    }

    [Fact] public void CoveringTheCaretMovesAutomaticBannerToAFreeCorner()
    {
        var work=new PixelBounds(0,0,1920,1040);
        var banner=new PixelBounds(653,720,614,300);
        var caret=new PixelBounds(900,900,2,22);
        var moved=BannerPositioning.AvoidCaret(work,banner,caret,22);
        Assert.False(BannerPositioning.Overlaps(moved,caret));
        Assert.True(moved.Left>=work.Left&&moved.Right<=work.Right);
        Assert.True(moved.Top>=work.Top&&moved.Bottom<=work.Bottom);
    }

    [Fact] public void AutomaticPlacementDoesNotJumpWhenItAlreadyAvoidsTheCaret()
    {
        var banner=new PixelBounds(653,720,614,300);
        Assert.Equal(banner,BannerPositioning.AvoidCaret(new(0,0,1920,1040),banner,new(100,100,2,22),22));
    }

    [Fact] public void InvalidMonitorBoundsAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>BannerPositioning.Clamp(new(0,0,0,100),new(0,0,600,300)));
    }
}
