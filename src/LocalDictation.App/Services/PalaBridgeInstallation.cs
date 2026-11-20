using System.Diagnostics;
using System.Reflection;

namespace LocalDictation.App.Services;

internal static class PalaBridgeInstallation
{
    public static string Profile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PalaBridge");

    public static ProcessStartInfo? ManagedStart(string scriptName, params string[] arguments)
    {
        var runtime = Path.Combine(Profile, "app", ".venv", "Scripts", "python.exe");
        var root = typeof(PalaBridgeInstallation).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "LocalDictation.SourceRoot")?.Value;
        if (string.IsNullOrWhiteSpace(root) || !File.Exists(runtime) ||
            !File.Exists(Path.Combine(Profile, "config.json")))
            return null;
        var script = Path.Combine(root, "devices", "kubo-note", "mac-bridge", scriptName);
        if (!File.Exists(script)) return null;

        var start = new ProcessStartInfo(runtime)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--managed");
        return start;
    }
}
