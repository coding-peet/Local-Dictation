using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;

namespace LocalDictation.App.Services;

public static class LocalApiLauncher
{
    public static async Task<bool> IsListening(Uri endpoint)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var client=new TcpClient();
        try{await client.ConnectAsync(endpoint.Host,endpoint.Port,timeout.Token);return true;}
        catch(SocketException error) when(error.SocketErrorCode==SocketError.ConnectionRefused){return false;}
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
