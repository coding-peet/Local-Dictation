using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LocalDictation.App.Services;

internal sealed class PalaUsbWatcher(Process process, string owner) : IDisposable
{
    public static PalaUsbWatcher? TryStart()
    {
        var start = PalaBridgeInstallation.ManagedStart("usb_sync.py", false, "--watch");
        if (start == null) return null;

        try
        {
            var owner = Guid.NewGuid().ToString("N");
            start.Environment["PALA_NOTE_SETTINGS_OWNER"] = owner;
            var process = Process.Start(start);
            return process is null ? null : new PalaUsbWatcher(process, owner);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Diagnostics.Error("Pala USB watcher startup", error);
            return null;
        }
    }

    public bool HasExited => process.HasExited;

    public bool OpenSettings()
    {
        var path = Path.Combine(PalaBridgeInstallation.Profile, "note-settings-runtime.json");
        try
        {
            if (process.HasExited) return false;
            using var file = File.OpenRead(path);
            using var document = JsonDocument.Parse(file);
            var record = document.RootElement;
            if (!record.TryGetProperty("Owner", out var value) || value.GetString() != owner ||
                !record.TryGetProperty("Url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) ||
                uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.UserInfo.Length != 0 ||
                uri.AbsolutePath != "/settings" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.Port < 1024)
                return false;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or
                                     InvalidOperationException or Win32Exception)
        {
            return false;
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
