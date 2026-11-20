using System.ComponentModel;
using System.Diagnostics;

namespace LocalDictation.App.Services;

internal sealed class PalaUsbWatcher(Process process) : IDisposable
{
    public static PalaUsbWatcher? TryStart()
    {
        var start = PalaBridgeInstallation.ManagedStart("usb_sync.py", "--watch");
        if (start == null) return null;

        try
        {
            var process = Process.Start(start);
            return process is null ? null : new PalaUsbWatcher(process);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Diagnostics.Error("Pala USB watcher startup", error);
            return null;
        }
    }

    public async Task StopAsync()
    {
        CloseControlPipe();
        try { await process.WaitForExitAsync(); }
        finally { process.Dispose(); }
    }

    public void Dispose()
    {
        CloseControlPipe();
        process.Dispose();
    }

    void CloseControlPipe()
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            Diagnostics.Error("Pala USB watcher stop", error);
        }
    }
}
