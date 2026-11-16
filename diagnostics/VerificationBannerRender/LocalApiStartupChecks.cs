using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using LocalDictation.App.Services;
using LocalDictation.Core;

public static class LocalApiStartupChecks
{
    static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    static Uri Endpoint(TcpListener listener)=>new("http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port);
    public static void Run(string output,List<string> checks)=>Task.Run(()=>Check(output,checks)).GetAwaiter().GetResult();
    static async Task Check(string output,List<string> checks)
    {
        foreach(var address in Socket.OSSupportsIPv6?new[]{IPAddress.Loopback,IPAddress.IPv6Loopback}:new[]{IPAddress.Loopback})
        {
            var listener=new TcpListener(address,0);listener.Start(1);
            try
            {
                var endpoint=Endpoint(listener);var starts=0;
                var result=await new LocalServiceStartup().EnsureStarted(endpoint,()=>LocalApiLauncher.IsListening(endpoint),()=>{starts++;return Task.CompletedTask;});
                Require(result==LocalServiceStartResult.AlreadyListening&&starts==0,"An existing listener was not preserved.");
                Require(!listener.Pending(),"Startup detection opened a TCP connection; a blocked handshake can still abort startup.");
                checks.Add("Passive "+address.AddressFamily+" occupied-port detection preserves the listener without a TCP handshake");
            }
            finally{listener.Stop();}
        }
        foreach(var unsupported in new[]{new Uri("relative",UriKind.Relative),new Uri("http://192.0.2.1:48125"),new Uri("http://127.0.0.1:0")})
        {
            var rejected=false;try{await LocalApiLauncher.IsListening(unsupported);}catch(ArgumentException){rejected=true;}
            Require(rejected,"The local listener probe accepted a nonlocal or relative endpoint.");
        }
        checks.Add("Local listener inspection rejects remote, relative and invalid-port endpoints without network access");
        var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var absent=Endpoint(reserve);reserve.Stop();
        var coordinator=new LocalServiceStartup();var launches=0;
        var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>coordinator.EnsureStarted(absent,()=>LocalApiLauncher.IsListening(absent),async()=>{Interlocked.Increment(ref launches);await Task.Yield();})));
        Require(launches==1&&results.Count(result=>result==LocalServiceStartResult.Started)==1&&results.Count(result=>result==LocalServiceStartResult.Starting)==7,"Concurrent requests launch more than once while an unbound service is preparing.");
        checks.Add("A missing listener starts once; concurrent requests retain the two-minute preparation guard");
        var uncertain=new LocalServiceStartup();var unsafeLaunches=0;var failed=false;
        try{await uncertain.EnsureStarted(absent,()=>throw new NetworkInformationException(5),()=>{unsafeLaunches++;return Task.CompletedTask;});}
        catch(NetworkInformationException){failed=true;}
        Require(failed&&unsafeLaunches==0,"An unknown port-table state was treated as permission to start another server.");
        Require(await uncertain.EnsureStarted(absent,()=>Task.FromResult(true),()=>throw new InvalidOperationException())==LocalServiceStartResult.AlreadyListening,"A failed port check stranded startup ownership.");
        checks.Add("Port-enumeration failure does not launch a server and releases startup ownership for retry");
        await CheckLauncher(output,checks);
    }
    static async Task CheckLauncher(string output,List<string> checks)
    {
        var root=typeof(LocalApiLauncher).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute=>attribute.Key=="LocalDictation.SourceRoot").Value!;
        var launcher=Path.Combine(root,"scripts","Start-CandidateServer.ps1");
        var fixture=Path.Combine(output,"startup-launcher-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(fixture,"env"));File.WriteAllText(Path.Combine(fixture,"env","pyvenv.cfg"),"synthetic guard fixture");
        var harness=Path.Combine(fixture,"check-launcher.ps1");
        File.WriteAllText(harness,"""
            param([string]$Launcher,[int]$Port,[string]$Fixture,[string]$Expected)
            $ErrorActionPreference='Stop'
            $global:ExpectedLauncherOutcome=$Expected;$global:FixturePort=$Port;$global:FixtureLaunches=0
            function global:Start-Process {
                param($FilePath,$ArgumentList,$WorkingDirectory,$WindowStyle,[switch]$PassThru,$RedirectStandardOutput,$RedirectStandardError)
                if($global:ExpectedLauncherOutcome -ne 'start'){throw 'A service launch escaped the startup guard fixture.'}
                if($FilePath -ne 'py' -or $WindowStyle -ne 'Hidden' -or $ArgumentList[-1] -ne $global:FixturePort){throw 'The launcher selected the wrong process, visibility or port.'}
                $global:FixtureLaunches++;[PSCustomObject]@{Id=123}
            }
            try {
                & $Launcher -Port $Port -Environment (Join-Path $Fixture 'env') -ModelRoot (Join-Path $Fixture 'models') -DataRoot (Join-Path $Fixture 'data')
                if($Expected -ne 'start' -or $global:FixtureLaunches -ne 1){throw 'The launcher did not produce its expected refusal or single simulated launch.'}
            } catch {
                if($Expected -eq 'start' -or $_.Exception.Message -notlike ('*'+$Expected+'*')){throw}
            }
            """);
        async Task RunLauncher(int port,string expected)
        {
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[]{"-NoProfile","-NonInteractive","-File",harness,"-Launcher",launcher,"-Port",port.ToString(),"-Fixture",fixture,"-Expected",expected})start.ArgumentList.Add(argument);
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try{await process.WaitForExitAsync(timeout.Token);}
            catch(OperationCanceledException){process.Kill(true);throw new InvalidOperationException("The owned launcher-guard helper did not finish.");}
            Require(process.ExitCode==0,"Launcher guard failed: "+await stderr);await stdout;
        }
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start(1);
        try
        {
            await RunLauncher(((IPEndPoint)listener.LocalEndpoint).Port,"already in use");
            Require(!listener.Pending(),"The launcher guard still relies on an active TCP handshake.");
            await RunLauncher(8123,"reserved");
            await RunLauncher(0,"Port");await RunLauncher(65536,"Port");
        }
        finally{listener.Stop();}
        Require(!Directory.Exists(Path.Combine(fixture,"logs")),"A refused launcher touched service output.");
        checks.Add("Actual PowerShell launcher refuses occupied and production ports without TCP connects or service processes; Start-Process is blocked in the isolated helper");
        var free=new TcpListener(IPAddress.Loopback,0);free.Start();var freePort=((IPEndPoint)free.LocalEndpoint).Port;free.Stop();
        await RunLauncher(freePort,"start");
        checks.Add("Actual PowerShell launcher reaches one hidden launch on a free port; the child helper substitutes Start-Process so no Python, models or API start");
    }
}
