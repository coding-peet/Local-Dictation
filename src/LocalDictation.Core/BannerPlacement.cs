namespace LocalDictation.Core;

public readonly record struct PixelBounds(int Left,int Top,int Width,int Height)
{
    public int Right=>Left+Width;
    public int Bottom=>Top+Height;
}

public static class BannerPlacement
{
    public static PixelBounds Calculate(PixelBounds work,double widthDip,double heightDip,double scale)
    {
        if(work.Width<=0||work.Height<=0||!double.IsFinite(scale)||scale<=0)
            throw new ArgumentException("Expected a valid monitor work area and DPI scale.");
        var margin=Math.Min((int)Math.Round(22*scale),Math.Min(work.Width,work.Height)/4);
        var width=Math.Clamp((int)Math.Round(widthDip*scale),1,Math.Max(1,work.Width-2*margin));
        var height=Math.Clamp((int)Math.Round(heightDip*scale),1,Math.Max(1,work.Height-2*margin));
        return new(work.Left+(work.Width-width)/2,work.Bottom-margin-height,width,height);
    }
}
