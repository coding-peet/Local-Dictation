namespace LocalDictation.Core;

public sealed record WindowsStartupLaunchPolicy(bool ShowSettings, bool ShowAlreadyRunningMessage)
{
    public static WindowsStartupLaunchPolicy FromArguments(IEnumerable<string> arguments)
    {
        var startup = arguments.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        return new(!startup, !startup);
    }
}
