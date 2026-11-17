namespace LocalDictation.Core;

public sealed record WindowsStartupState(bool Enabled, bool CanChange, string Message);
public sealed record WindowsStartupChangeResult(bool Success, WindowsStartupState State, string Error);

/// <summary>A store scoped to the single Local Dictation current-user startup value.</summary>
public interface IWindowsStartupStore
{
    object? Read();
    void Write(string command);
    void Delete();
}

/// <summary>Reads actual registration; only an explicit toggle can mutate the store.</summary>
public sealed class WindowsStartupRegistration(IWindowsStartupStore store, string? executablePath)
{
    readonly string? command = StartupCommand(executablePath);
    bool lastVerifiedEnabled, hasVerifiedState;

    public WindowsStartupState Read()
    {
        if (command == null)
            return new(false, false, "Open the installed Local Dictation app to change Windows startup.");
        // The Run limit applies to the complete quoted command, including arguments.
        if (command.Length > 260)
            return new(false, false, "The installed app path is too long for Windows startup. Move the installed app to a shorter path, then reopen it.");
        try
        {
            var value = store.Read();
            hasVerifiedState = true;
            if (value == null)
            {
                lastVerifiedEnabled = false;
                return new(false, true, "Starts only when you open the app.");
            }
            if (value is string text && string.Equals(text, command, StringComparison.OrdinalIgnoreCase))
            {
                lastVerifiedEnabled = true;
                return new(true, true, "Opens in the system tray when you sign in to Windows.");
            }
            lastVerifiedEnabled = false;
            return new(false, false, "Another startup command uses this app's entry. It was preserved. Check Windows startup settings before changing it.");
        }
        catch (Exception)
        {
            var knownState = hasVerifiedState ? "The switch shows the last verified state." : "Its current state is unknown.";
            return new(lastVerifiedEnabled, false,
                "Windows startup could not be read. " + knownState + " Reopen General to retry or check Windows access.");
        }
    }

    public WindowsStartupChangeResult SetEnabled(bool enabled)
    {
        var before = Read();
        if (!before.CanChange) return new(false, before, before.Message);
        if (before.Enabled == enabled) return new(true, before, "");
        try
        {
            if (enabled) store.Write(command!);
            else store.Delete();
        }
        catch (Exception)
        {
            return new(false, Read(),
                "Windows startup could not be changed. Check Windows access or organization restrictions, then retry. The switch shows the last verified state.");
        }
        var actual = Read();
        if (!actual.CanChange)
            return new(false, actual, actual.Message);
        if (actual.Enabled != enabled)
            return new(false, actual,
                "Windows did not keep the startup change. Check Windows startup settings or organization restrictions, then retry.");
        return new(true, actual, "");
    }

    static string? StartupCommand(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable != executable.Trim() ||
            executable.Any(character => char.IsControl(character) || "\"<>|?*".Contains(character))) return null;
        try
        {
            if (!Path.IsPathFullyQualified(executable) ||
                !string.Equals(Path.GetFileName(executable), "LocalDictation.exe", StringComparison.OrdinalIgnoreCase)) return null;
            return "\"" + Path.GetFullPath(executable) + "\" --startup";
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
