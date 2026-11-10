using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

public sealed class GlobalInput : IDisposable
{
    public static readonly nuint InjectionTag = 0x4C444943;
    readonly Hook keyboardCallback, mouseCallback;
    readonly InputCommands commands = new();
    public DestinationEpoch Epoch {get;}=new();
    public bool SessionActive {get;set;}
    public bool FocusTrackingAvailable=>focusHook!=0;
    public event Action? Cancel;
    readonly FocusCallback focusCallback;
    nint focusHook;
    delegate void FocusCallback(nint hook,uint evt,nint hwnd,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")] static extern nint SetWinEventHook(uint min,uint max,nint module,FocusCallback callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(nint hook);
    nint keyboard, mouse;
    public event Action? Toggle;
    public event Action? UserEdited;
    public bool ShortcutEnabled { get; set; }
    delegate nint Hook(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    struct KeyboardData { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct MouseData { public int X, Y; public uint Mouse, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern nint SetWindowsHookEx(int id, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandle(string? module);

    public GlobalInput()
    {
        focusCallback=(_,_,_,_,_,_,_)=>ObserveEdit();
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
                if (pressed && key.Key is not (0x10 or 0x11 or 0xA0 or 0xA1 or 0xA2 or 0xA3))
                    ObserveEdit();
                var decision=commands.Update((int)key.Key,pressed,SessionActive,ShortcutEnabled);
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
            if (mouseData.Extra != InjectionTag) ObserveEdit();
        }
        return CallNextHookEx(mouse, code, message, data);
    }
    void ObserveEdit(){Epoch.UserInput();UserEdited?.Invoke();}
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
