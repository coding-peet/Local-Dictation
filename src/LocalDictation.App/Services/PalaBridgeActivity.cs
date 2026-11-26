using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace LocalDictation.App.Services;

internal static class PalaBridgeActivity
{
    sealed record Snapshot(int Pid, string Transport, string Phase, DateTimeOffset UpdatedAt);

    public static string? ReadCurrent(string? profile = null)
    {
        profile ??= PalaBridgeInstallation.Profile;
        if (!Directory.Exists(profile)) return null;

        Snapshot? newest = null;
        string? display = null;
        try
        {
            foreach (var file in Directory.EnumerateFiles(profile, "bridge-activity-*.json"))
            {
                var snapshot = Read(file);
                if (snapshot == null || (newest != null && snapshot.UpdatedAt <= newest.UpdatedAt))
                    continue;
                var label = Format(snapshot.Phase, snapshot.Transport);
                if (label == null || !IsLive(snapshot)) continue;
                newest = snapshot;
                display = label;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return display;
    }

    static Snapshot? Read(string file)
    {
        try
        {
            using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<Snapshot>(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    static bool IsLive(Snapshot snapshot)
    {
        try
        {
            using var process = Process.GetProcessById(snapshot.Pid);
            return !process.HasExited && snapshot.UpdatedAt >= process.StartTime.ToUniversalTime().AddSeconds(-1);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    static string? Format(string phase, string transport)
    {
        var label = phase switch
        {
            "receiving" => "Receiving recording",
            "waiting" => "Waiting for Local Dictation",
            "transcribing" => "Transcribing on this PC",
            "processing" => "Preparing note",
            "returning" => "Sending transcript to the Note",
            "settings" => "Syncing name and weather",
            "weather" => "Fetching weather",
            "google" => "Saving to Google",
            _ => null
        };
        if (label == null) return null;
        return transport switch
        {
            "usb" => label + " · USB",
            "bluetooth" => label + " · Bluetooth",
            "network" => label + " · Wi-Fi",
            "delivery" => label,
            _ => null
        };
    }
}
