namespace LocalDictation.Core;

/// <summary>Physical-pixel geometry for a manually placed, non-activating banner.</summary>
public static class BannerPositioning
{
    public static PixelBounds Clamp(PixelBounds work,PixelBounds requested)
    {
        if(work.Width<=0||work.Height<=0)throw new ArgumentException("Expected a valid monitor work area.");
        var width=Math.Clamp(requested.Width,1,work.Width);
        var height=Math.Clamp(requested.Height,1,work.Height);
        return new(Math.Clamp(requested.Left,work.Left,work.Right-width),
            Math.Clamp(requested.Top,work.Top,work.Bottom-height),width,height);
    }

    public static bool Overlaps(PixelBounds a,PixelBounds b) =>
        a.Left<b.Right&&a.Right>b.Left&&a.Top<b.Bottom&&a.Bottom>b.Top;

    public static PixelBounds AvoidCaret(PixelBounds work,PixelBounds banner,PixelBounds caret,int gap)
    {
        banner=Clamp(work,banner);
        gap=Math.Max(0,gap);
        var protectedCaret=new PixelBounds(caret.Left-gap,caret.Top-gap,Math.Max(1,caret.Width)+2*gap,Math.Max(1,caret.Height)+2*gap);
        if(!Overlaps(banner,protectedCaret))return banner;
        var candidates=new[]{
            new PixelBounds(work.Left+gap,work.Top+gap,banner.Width,banner.Height),
            new PixelBounds(work.Right-gap-banner.Width,work.Top+gap,banner.Width,banner.Height),
            new PixelBounds(work.Left+gap,work.Bottom-gap-banner.Height,banner.Width,banner.Height),
            new PixelBounds(work.Right-gap-banner.Width,work.Bottom-gap-banner.Height,banner.Width,banner.Height)};
        // A small native caret gives a reliable region to avoid; other editors
        // may not expose one, in which case the caller preserves normal placement.
        return candidates.Select(p=>Clamp(work,p)).Where(p=>!Overlaps(p,protectedCaret))
            .OrderByDescending(p=>Math.Pow(p.Left+p.Width/2.0-caret.Left,2)+Math.Pow(p.Top+p.Height/2.0-caret.Top,2))
            .DefaultIfEmpty(banner).First();
    }
}
