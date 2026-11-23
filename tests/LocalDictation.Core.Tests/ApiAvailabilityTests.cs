using System.Net;
using LocalDictation.Core;
using Xunit;

public sealed class ApiAvailabilityTests
{
    sealed class SpeechHandler(string expectedKey) : HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;
            Assert.Equal("/v1/audio/speech",request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer",request.Headers.Authorization?.Scheme);
            Assert.Equal(expectedKey,request.Headers.Authorization?.Parameter);
            var content=new ByteArrayContent("RIFF"u8.ToArray());
            content.Headers.ContentType=new("audio/wav");
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=content};
            response.Headers.Add("X-Speech-Backend","cpu");
            return Task.FromResult(response);
        }
    }

    [Fact] public async Task SpeechUsesApplicationKeyOnlyForItsConfiguredOrigin()
    {
        var key=new string('k',43);
        var handler=new SpeechHandler(key);
        using var client=new ApiClient(new Settings(),handler,TimeSpan.FromSeconds(5),key);
        var result=await client.Synthesize("Ahoj.");
        Assert.Equal("RIFF"u8.ToArray(),result.Wave);
        Assert.Equal("cpu",result.Backend);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>client.Get("http://127.0.0.1:48126/health"));
        Assert.Equal(1,handler.Calls);
    }

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
