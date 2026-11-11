using System.Runtime.InteropServices;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

public static class BannerMonitor
{
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] struct GuiThreadInfo
    {public int Size;public uint Flags;public nint Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Rect CaretBounds;}
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint window,uint flags);
    [DllImport("user32.dll")] static extern nint MonitorFromRect(ref Rect bounds,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window,out Rect bounds);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
    [DllImport("user32.dll")] static extern bool ClientToScreen(nint window,ref Point point);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);

    public static PixelBounds? Bounds(nint window)=>GetWindowRect(window,out var bounds)
        ?new(bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top):null;

    public static void Move(nint banner,int left,int top)=>SetWindowPos(banner,0,left,top,0,0,0x15);

    static PixelBounds? NativeCaret(nint destination)
    {
        if(destination==0||!IsWindow(destination))return null;
        var thread=GetWindowThreadProcessId(destination,out _);
        var info=new GuiThreadInfo{Size=Marshal.SizeOf<GuiThreadInfo>()};
        if(thread==0||!GetGUIThreadInfo(thread,ref info)||info.Caret==0)return null;
        var point=new Point{X=info.CaretBounds.Left,Y=info.CaretBounds.Top};
        if(!ClientToScreen(info.Caret,ref point))return null;
        return new(point.X,point.Y,Math.Max(2,info.CaretBounds.Right-info.CaretBounds.Left),Math.Max(16,info.CaretBounds.Bottom-info.CaretBounds.Top));
    }

    public static PixelBounds? Position(nint banner,nint destination,double widthDip,double heightDip,PixelBounds? manual=null)
    {
        if(banner==0)return null;
        var target=MonitorFromWindow(IsWindow(destination)?destination:banner,2);
        if(manual is {} preferred)
        {
            var requested=new Rect{Left=preferred.Left,Top=preferred.Top,Right=preferred.Right,Bottom=preferred.Bottom};
            target=MonitorFromRect(ref requested,2);
        }
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(target,ref info))return null;
        var work=new PixelBounds(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top);
        if(MonitorFromWindow(banner,2)!=target)
        {
            // Move the owned non-activating window first, then read its own PMv2 DPI.
            SetWindowPos(banner,0,work.Left+8,work.Top+8,0,0,0x15);
        }
        var scale=Math.Max(96,GetDpiForWindow(banner))/96.0;
        var desired=BannerPlacement.Calculate(work,widthDip,heightDip,scale);
        if(manual is {} saved)desired=BannerPositioning.Clamp(work,new(saved.Left,saved.Top,desired.Width,desired.Height));
        else if(NativeCaret(destination) is {} caret)desired=BannerPositioning.AvoidCaret(work,desired,caret,(int)Math.Round(22*scale));
        if(GetWindowRect(banner,out var current)&&current.Left==desired.Left&&current.Top==desired.Top&&
            current.Right-current.Left==desired.Width&&current.Bottom-current.Top==desired.Height)return desired;
        SetWindowPos(banner,0,desired.Left,desired.Top,desired.Width,desired.Height,0x14);
        return desired;
    }
}
