using System.Net;
using LocalDictation.Core;
using Xunit;

public sealed class ApiAvailabilityTests
{
    sealed class Handler(Func<CancellationToken,Task<HttpResponseMessage>> reply):HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get,request.Method);Assert.Equal("/health",request.RequestUri!.AbsolutePath);
            Calls++;return reply(token);
        }
    }
    static HttpResponseMessage Health(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body)};

    [Fact] public async Task ReplayWaitsForRuntimeReadinessAndReleasedOwnership()
    {
        var replies=new Queue<string>(["{\"ready\":false,\"busy\":false}","{\"ready\":true,\"busy\":true}","{\"ready\":true}","{\"ready\":true,\"busy\":false}"]);
        var handler=new Handler(_=>Task.FromResult(Health(replies.Dequeue())));
        using var client=new ApiClient(new Settings(),handler);
        await client.WaitForIdle(CancellationToken.None,pollDelay:TimeSpan.Zero);
        Assert.Equal(4,handler.Calls);Assert.Empty(replies);
    }
    [Fact] public async Task UserCancellationStopsWaitingWithoutAnotherRequest()
    {
        using var cancel=new CancellationTokenSource();
        var handler=new Handler(_=>{cancel.Cancel();return Task.FromResult(Health("{\"ready\":true,\"busy\":true}"));});
        using var client=new ApiClient(new Settings(),handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.WaitForIdle(cancel.Token,pollDelay:TimeSpan.Zero));
        Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task AbsoluteWaitDeadlineCancelsAnUnresponsiveHealthRequest()
    {
        CancellationToken requestToken=default;
        var handler=new Handler(async token=>{requestToken=token;await Task.Delay(Timeout.InfiniteTimeSpan,token);return Health("{}");});
        using var client=new ApiClient(new Settings(),handler);
        await Assert.ThrowsAsync<TimeoutException>(()=>client.WaitForIdle(CancellationToken.None,TimeSpan.FromMilliseconds(50),TimeSpan.Zero));
        Assert.True(requestToken.IsCancellationRequested);Assert.Equal(1,handler.Calls);
    }
}
