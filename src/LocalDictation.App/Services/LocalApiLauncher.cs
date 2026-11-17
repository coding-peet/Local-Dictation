using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;

namespace LocalDictation.App.Services;

public static class LocalApiLauncher
{
    public static Task<bool> IsListening(Uri endpoint)
    {
        if(!endpoint.IsAbsoluteUri||!endpoint.IsLoopback||endpoint.Port<1)throw new ArgumentException("Only local service endpoints with a valid port can be inspected.",nameof(endpoint));
        // A listener can exist even when a TCP handshake times out. Preserve any
        // binding on this port; enumeration errors must not imply a free port.
        var listeners=IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return Task.FromResult(listeners.Any(listener=>listener.Port==endpoint.Port));
    }
    public static async Task Start(int port)
    {
        var root=typeof(LocalApiLauncher).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute=>attribute.Key=="LocalDictation.SourceRoot")?.Value;
        var script=string.IsNullOrWhiteSpace(root)?null:Path.Combine(root,"scripts","Start-CandidateServer.ps1");
        if(script is null||!File.Exists(script))throw new IOException("The service launcher is unavailable in this build's source checkout.");
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",script,"-Port",port.ToString(System.Globalization.CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("The service launcher could not start.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();await output;await error;
        if(process.ExitCode!=0)throw new IOException("The service launcher did not complete. Check the local API logs and try Start local API again.");
    }
}
