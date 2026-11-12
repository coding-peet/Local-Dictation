using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LocalDictation.App.Services;

public static class WindowsDestinationApplication
{
    // The captured top-level HWND identifies the application. Never inspect
    // window titles, field names, message text or other application content.
    public static string? ProcessName(nint topLevelWindow)
    {
        if(topLevelWindow==0)return null;
        try
        {
            if(GetWindowThreadProcessId(topLevelWindow,out var processId)==0||processId==0||processId>int.MaxValue)return null;
            using var process=Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch(Exception){return null;}
    }

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window,out uint processId);
}
