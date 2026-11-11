using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

public readonly record struct DestinationInput(string Reason,bool FocusOnly,nint RootWindow);

public sealed class GlobalInput : IDisposable
{
    public static readonly nuint InjectionTag = 0x4C444943;
    readonly Hook keyboardCallback, mouseCallback;
    readonly InputCommands commands = new();
    public DestinationEpoch Epoch {get;}=new();
    public bool SessionActive {get;set;}
    public nint NonActivatingWindow {get;set;}
    public bool FocusTrackingAvailable=>focusHook!=0;
    public event Action? Cancel;
    readonly FocusCallback focusCallback;
    nint focusHook;
    delegate void FocusCallback(nint hook,uint evt,nint hwnd,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")] static extern nint SetWinEventHook(uint min,uint max,nint module,FocusCallback callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(nint hook);
    nint keyboard, mouse;
    public event Action? Toggle;
    public event Action<DestinationInput>? UserEdited;
    public bool ShortcutEnabled { get; set; }
    public ShortcutSpec Shortcut {get=>commands.Shortcut;set=>commands.Shortcut=value;}
    delegate nint Hook(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    struct KeyboardData { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct MouseData { public int X, Y; public uint Mouse, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern nint SetWindowsHookEx(int id, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandle(string? module);
    [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
    [DllImport("user32.dll")] static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint window,uint flags);
    static nint Root(nint window)=>window==0?0:GetAncestor(window,2);

    public GlobalInput()
    {
        focusCallback=(_,_,window,_,_,_,_)=>ObserveEdit("Cursor delivery paused. Return to the original field to resume.",Root(window),true);
        focusHook=SetWinEventHook(0x8005,0x8005,0,focusCallback,0,0,0);
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
        keyboard = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
        mouse = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
        if (keyboard == 0 || mouse == 0)
        {
            Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The global input observer could not start.");
        }
    }
    nint Keyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KeyboardData>(data);
            if (key.Extra != InjectionTag)
            {
                var pressed = message == 0x100 || message == 0x104;
                var decision=commands.Update((int)key.Key,pressed,SessionActive,ShortcutEnabled);
                if(pressed&&!decision.Suppress&&!ShortcutSpec.IsModifier((int)key.Key))ObserveEdit("Cursor delivery paused while you type. Return to the original unchanged field to resume.");
                if(decision.Command==InputCommand.Toggle)Toggle?.Invoke();
                if(decision.Command==InputCommand.Cancel)Cancel?.Invoke();
                if(decision.Suppress)return 1;
            }
        }
        return CallNextHookEx(keyboard, code, message, data);
    }
    nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0 && (message == 0x201 || message == 0x204 || message == 0x207 || message == 0x20B))
        {
            var mouseData = Marshal.PtrToStructure<MouseData>(data);
            if (mouseData.Extra != InjectionTag) ObserveEdit("Cursor delivery paused. Return to the original field to resume.",
                Root(WindowFromPoint(new(){X=mouseData.X,Y=mouseData.Y})));
        }
        return CallNextHookEx(mouse, code, message, data);
    }
    void ObserveEdit(string reason,nint observedWindow=0,bool focusOnly=false)
    {
        if(Epoch.UserInput((long)observedWindow,(long)NonActivatingWindow))UserEdited?.Invoke(new(reason,focusOnly,observedWindow));
    }
    public void Dispose()
    {
        if (keyboard != 0) UnhookWindowsHookEx(keyboard);
        if (mouse != 0) UnhookWindowsHookEx(mouse);
        keyboard = mouse = 0;
        if(focusHook!=0)UnhookWinEvent(focusHook);focusHook=0;
    }
    public static bool TypeWhisperRunning() =>
        Process.GetProcesses().Any(p => { try { return p.ProcessName.Contains("typewhisper", StringComparison.OrdinalIgnoreCase); } catch { return false; } });
}
