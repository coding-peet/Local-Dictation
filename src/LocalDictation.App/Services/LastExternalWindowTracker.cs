using System.Runtime.InteropServices;
using System.Text;

namespace LocalDictation.App.Services;

/// <summary>Observes foreground window/process metadata only; never reads field text.</summary>
public sealed class LastExternalWindowTracker : IDisposable
{
    readonly WinEvent callback;
    nint hook;
    readonly HistoryDestinationMemory memory=new(Environment.ProcessId);
    delegate void WinEvent(nint hook,uint evt,nint window,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")]static extern nint SetWinEventHook(uint min,uint max,nint module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")]static extern nint GetAncestor(nint window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(nint window,StringBuilder name,int length);
    public bool IsAvailable=>hook!=0;
    public HistoryInsertionDestination? Current=>hook==0?null:memory.Current;
    public LastExternalWindowTracker()
    {
        callback=(_,_,window,_,_,_,_)=>Observe(window);
        // EVENT_SYSTEM_FOREGROUND only. Keep recording/input hooks independent.
        hook=SetWinEventHook(3,3,0,callback,0,0,0);
        if(hook!=0)Observe(WindowsTextDelivery.ForegroundWindow);
    }
    void Observe(nint window)
    {
        window=GetAncestor(window,2);if(window==0)return;
        GetWindowThreadProcessId(window,out var process);
        if(process==0||process==Environment.ProcessId)return;
        // Queued foreground events preserve their order even after our own UI opens.
        var windowClass=new StringBuilder(256);
        if(GetClassName(window,windowClass,windowClass.Capacity)==0)return;
        memory.Observe(window,(int)process,windowClass.ToString());
    }
    public void Dispose(){if(hook!=0)UnhookWinEvent(hook);hook=0;memory.Clear();}
}
