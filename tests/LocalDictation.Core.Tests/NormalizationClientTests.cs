using System.Net;
using System.Net.Http.Headers;
using LocalDictation.Core;
using Xunit;
public class NormalizationClientTests
{
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(action(r));}}
    [Fact] public async Task OlderApiHasActionableErrorAndSourceBytesArePreserved()
    {
        var source=new byte[]{1,2,3};using var api=new ApiClient(new Settings(),new Handler(r=>new(HttpStatusCode.NotFound)));
        var error=await Assert.ThrowsAsync<AudioImportException>(()=>api.NormalizeRecording(source,CancellationToken.None));Assert.Contains("API",error.Message);Assert.Equal(new byte[]{1,2,3},source);
    }
    [Fact] public async Task ResponseTypeAndLengthAreBoundedBeforeReading()
    {
        using var api=new ApiClient(new Settings(),new Handler(r=>{var data=new ByteArrayContent([1]);data.Headers.ContentType=new("audio/wav");data.Headers.ContentLength=600*32000+45;return new(HttpStatusCode.OK){Content=data};}));
        await Assert.ThrowsAsync<InvalidDataException>(()=>api.NormalizeRecording([1,2],CancellationToken.None));
    }
}
