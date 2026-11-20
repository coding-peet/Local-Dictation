using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace LocalDictation.App.Services;

internal sealed class PalaBridgeController : IDisposable
{
    internal sealed record RuntimeStatus(int Pid, string? Owner, string State, DateTimeOffset UpdatedAt,
        string[] Listeners, DateTimeOffset? LastContact, string? Error);

    internal sealed record Status(string Summary, string? Error, DateTimeOffset? LastContact,
        bool CanStart, bool CanStop);

    readonly ProcessStartInfo? start = PalaBridgeInstallation.ManagedStart("bridge.py");
    Process? process;
    string? owner;
    string? lastError;
    bool stopping;
    bool enabled = true;
    bool disposed;

    public string? ProfileModel { get; } = ReadProfileModel();
    public bool HasProfile => File.Exists(Path.Combine(PalaBridgeInstallation.Profile, "config.json"));

    public Status ReadStatus()
    {
        var runtime = ReadRuntime();
        var owned = process != null && !process.HasExited;
        if (process != null && !owned && !stopping)
        {
            if (process.ExitCode != 0)
                lastError = runtime?.Owner == owner && runtime?.Error is { } error
                    ? error : $"Bridge exited with code {process.ExitCode}. Check the pairing and Bridge runtime.";
            process.Dispose();
            process = null;
        }

        if (stopping) return new("Stopping Bridge…", lastError, null, false, false);
        if (!enabled) return new("Pala sync is disabled.", null, null, false, false);
        if (runtime != null && IsLive(runtime))
        {
            var managed = owned && runtime.Owner == owner;
            var description = runtime.State == "starting" ? "Starting Bridge…" :
                runtime.Listeners is { Length: > 0 }
                    ? "HTTPS " + string.Join(", ", runtime.Listeners.Select(address => address + ":8443"))
                    : "Running. Waiting for an approved network.";
            if (!managed) description = "Standalone Bridge · " + description;
            return new(description, runtime.Error ?? lastError, runtime.LastContact, false, managed);
        }
        if (owned) return new("Starting Bridge or waiting for status…", lastError, null, false, true);
        if (PortInUse()) return new("Port 8443 is in use. Check the standalone Bridge.",
            lastError, null, false, false);
        if (!HasProfile) return new("No Bridge pairing profile found.", lastError, null, false, false);
        if (start == null) return new("The Bridge runtime or source files are missing.", lastError, null, false, false);
        return new("No managed Bridge running.", lastError, null, true, false);
    }

    public void SetEnabled(bool integrationEnabled)
    {
        enabled = integrationEnabled;
        if (!enabled) _ = StopAsync();
    }

    public void Start()
    {
        if (disposed || start == null || !ReadStatus().CanStart) return;
        lastError = null;
        owner = Guid.NewGuid().ToString("N");
        start.Environment["PALA_BRIDGE_MANAGED_OWNER"] = owner;
        try
        {
            process = Process.Start(start);
            if (process == null) lastError = "The Bridge process did not start.";
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Diagnostics.Error("Pala Bridge startup", error);
            lastError = "Windows could not start the Bridge runtime.";
        }
    }

    public async Task StopAsync(bool restart = false)
    {
        if (stopping || disposed || process is not { HasExited: false } current) return;
        stopping = true;
        try
        {
            CloseControlPipe(current);
            await current.WaitForExitAsync();
            process = null;
            current.Dispose();
        }
        catch (InvalidOperationException) when (disposed)
        {
            return;
        }
        finally { stopping = false; }
        if (restart && !disposed) Start();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (process == null) return;
        CloseControlPipe(process);
        if (!stopping) process.Dispose();
    }

    static void CloseControlPipe(Process current)
    {
        try { current.StandardInput.Close(); }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            Diagnostics.Error("Pala Bridge stop", error);
        }
    }

    static RuntimeStatus? ReadRuntime()
    {
        var path = Path.Combine(PalaBridgeInstallation.Profile, "bridge-runtime.json");
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<RuntimeStatus>(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    static string? ReadProfileModel()
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(PalaBridgeInstallation.Profile, "config.json"));
            using var config = JsonDocument.Parse(stream);
            return config.RootElement.ValueKind == JsonValueKind.Object &&
                config.RootElement.TryGetProperty("transcription_model", out var model) &&
                model.ValueKind == JsonValueKind.String ? model.GetString() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    static bool IsLive(RuntimeStatus status)
    {
        if (status.State is not ("running" or "starting") ||
            status.UpdatedAt < DateTimeOffset.UtcNow.AddSeconds(-15)) return false;
        try
        {
            using var process = Process.GetProcessById(status.Pid);
            return !process.HasExited &&
                status.UpdatedAt >= process.StartTime.ToUniversalTime().AddSeconds(-1);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    static bool PortInUse()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(address => address.Port == 8443);
        }
        catch (NetworkInformationException) { return false; }
    }
}
