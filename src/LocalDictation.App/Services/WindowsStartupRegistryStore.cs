using LocalDictation.Core;
using Microsoft.Win32;

namespace LocalDictation.App.Services;

/// <summary>Only Local Dictation's own HKCU Run value; reads never create registry keys.</summary>
public sealed class WindowsStartupRegistryStore(string? executablePath) : IWindowsStartupStore
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "LocalDictation";
    readonly string? expectedCommand = executablePath == null ? null : "\"" + Path.GetFullPath(executablePath) + "\" --startup";

    public object? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return ReadValue(key);
    }

    public void Write(string command)
    {
        if (!IsOwned(command)) throw new InvalidOperationException("The startup command does not belong to this executable.");
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ??
            Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        var existing = ReadValue(key);
        if (existing != null && !IsOwned(existing))
            throw new InvalidOperationException("The existing startup command was preserved.");
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        var existing = ReadValue(key);
        if (existing == null) return;
        if (!IsOwned(existing)) throw new InvalidOperationException("The existing startup command was preserved.");
        key!.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    bool IsOwned(object value) => expectedCommand != null && value is string command &&
        string.Equals(command, expectedCommand, StringComparison.OrdinalIgnoreCase);

    static object? ReadValue(RegistryKey? key) =>
        key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
}
