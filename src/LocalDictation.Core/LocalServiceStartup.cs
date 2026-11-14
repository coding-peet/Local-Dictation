using System.Diagnostics;

namespace LocalDictation.Core;

public enum LocalServiceStartResult {AlreadyListening,Started,Starting,UnsupportedEndpoint}

/// <summary>Serialize launch attempts and cover the interval before a child binds its port.</summary>
public sealed class LocalServiceStartup
{
    readonly SemaphoreSlim gate=new(1,1);
    readonly Dictionary<int,TimeSpan> launched=[];
    readonly Func<TimeSpan> clock;
    public LocalServiceStartup(Func<TimeSpan>? clock=null)=>this.clock=clock??(()=>Stopwatch.GetElapsedTime(0));
    public async Task<LocalServiceStartResult> EnsureStarted(Uri endpoint,Func<Task<bool>> isListening,Func<Task> launch)
    {
        if(!endpoint.IsAbsoluteUri||endpoint.Scheme!="http"||endpoint.Host is not ("127.0.0.1" or "localhost")||
            endpoint.Port==8123||endpoint.AbsolutePath!="/"||endpoint.UserInfo.Length>0||endpoint.Query.Length>0||endpoint.Fragment.Length>0)
            return LocalServiceStartResult.UnsupportedEndpoint;
        await gate.WaitAsync();
        try
        {
            if(await isListening()){launched.Remove(endpoint.Port);return LocalServiceStartResult.AlreadyListening;}
            if(launched.TryGetValue(endpoint.Port,out var previous)&&clock()-previous<TimeSpan.FromMinutes(2))
                return LocalServiceStartResult.Starting;
            await launch();
            launched[endpoint.Port]=clock();
            return LocalServiceStartResult.Started;
        }
        finally{gate.Release();}
    }
}
