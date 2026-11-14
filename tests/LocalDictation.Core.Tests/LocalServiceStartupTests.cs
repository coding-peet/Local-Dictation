using LocalDictation.Core;
using Xunit;

public class LocalServiceStartupTests
{
    static readonly Uri Endpoint=new("http://127.0.0.1:48125");
    [Fact] public async Task ExistingListenerIsPreserved()
    {var starts=0;var result=await new LocalServiceStartup().EnsureStarted(Endpoint,()=>Task.FromResult(true),()=>{starts++;return Task.CompletedTask;});Assert.Equal(LocalServiceStartResult.AlreadyListening,result);Assert.Equal(0,starts);}
    [Fact] public async Task MissingListenerStartsOnceWhileItWarmsUp()
    {
        var coordinator=new LocalServiceStartup();var starts=0;
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests=Enumerable.Range(0,8).Select(_=>coordinator.EnsureStarted(Endpoint,()=>Task.FromResult(false),async()=>{Interlocked.Increment(ref starts);await release.Task;})).ToArray();
        Assert.Equal(1,starts);release.SetResult();
        var results=await Task.WhenAll(requests);
        Assert.Equal(1,starts);Assert.Single(results,r=>r==LocalServiceStartResult.Started);
    }
    [Fact] public async Task FailedLaunchCanBeRetried()
    {
        var coordinator=new LocalServiceStartup();
        await Assert.ThrowsAsync<IOException>(()=>coordinator.EnsureStarted(Endpoint,()=>Task.FromResult(false),()=>throw new IOException("fixture")));
        var starts=0;await coordinator.EnsureStarted(Endpoint,()=>Task.FromResult(false),()=>{starts++;return Task.CompletedTask;});Assert.Equal(1,starts);
    }
    [Fact] public async Task ExpiredGracePeriodAllowsRetryAtTheSelectedEndpoint()
    {
        var now=TimeSpan.Zero;var coordinator=new LocalServiceStartup(()=>now);var starts=0;
        Task Launch(){starts++;return Task.CompletedTask;}
        await coordinator.EnsureStarted(Endpoint,()=>Task.FromResult(false),Launch);
        now=TimeSpan.FromSeconds(119);await coordinator.EnsureStarted(Endpoint,()=>Task.FromResult(false),Launch);Assert.Equal(1,starts);
        now=TimeSpan.FromSeconds(121);await coordinator.EnsureStarted(Endpoint,()=>Task.FromResult(false),Launch);Assert.Equal(2,starts);
        await coordinator.EnsureStarted(new Uri("http://127.0.0.1:48126"),()=>Task.FromResult(false),Launch);Assert.Equal(3,starts);
    }
    [Theory]
    [InlineData("http://127.0.0.1:8123")]
    [InlineData("https://127.0.0.1:48125")]
    [InlineData("http://example.com:48125")]
    [InlineData("http://127.0.0.1:48125/path")]
    [InlineData("http://127.0.0.1:48125/?query=1")]
    public async Task UnsupportedEndpointsNeverLaunch(string value)
    {var calls=0;var result=await new LocalServiceStartup().EnsureStarted(new(value),()=>{calls++;return Task.FromResult(false);},()=>{calls++;return Task.CompletedTask;});Assert.Equal(LocalServiceStartResult.UnsupportedEndpoint,result);Assert.Equal(0,calls);}
}
