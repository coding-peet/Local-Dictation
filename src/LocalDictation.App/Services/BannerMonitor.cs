using System.Runtime.InteropServices;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

public static class BannerMonitor
{
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window,out Rect bounds);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);

    public static void Position(nint banner,nint destination,double widthDip,double heightDip)
    {
        if(banner==0)return;
        var target=MonitorFromWindow(IsWindow(destination)?destination:banner,2);
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(target,ref info))return;
        var work=new PixelBounds(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top);
        if(MonitorFromWindow(banner,2)!=target)
        {
            // Move the owned non-activating window first, then read its own PMv2 DPI.
            SetWindowPos(banner,0,work.Left+8,work.Top+8,0,0,0x15);
        }
        var scale=Math.Max(96,GetDpiForWindow(banner))/96.0;
        var desired=BannerPlacement.Calculate(work,widthDip,heightDip,scale);
        if(GetWindowRect(banner,out var current)&&current.Left==desired.Left&&current.Top==desired.Top&&
            current.Right-current.Left==desired.Width&&current.Bottom-current.Top==desired.Height)return;
        SetWindowPos(banner,0,desired.Left,desired.Top,desired.Width,desired.Height,0x14);
    }
}
