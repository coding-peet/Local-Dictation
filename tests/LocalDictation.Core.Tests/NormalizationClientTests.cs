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
        var error=await Assert.ThrowsAsync<AudioImportException>(()=>api.NormalizeRecording([1,2],CancellationToken.None));Assert.Contains("local API duration limit",error.Message);
    }
    [Theory]
    [InlineData(400,"supported WAV")]
    [InlineData(413,"too large")]
    [InlineData(409,"busy or preparing")]
    [InlineData(503,"local API is unavailable")]
    [InlineData(504,"did not respond in time")]
    [InlineData(500,"local API could not prepare")]
    public async Task HttpFailuresUseStatusMessagesWithoutExposingServerBody(int status,string expected)
    {
        using var api=new ApiClient(new Settings(),new Handler(r=>new((HttpStatusCode)status){Content=new StringContent("{\"error\":{\"message\":\"SECRET path and transcript\"}} ")}));
        var error=await Assert.ThrowsAsync<AudioImportException>(()=>api.NormalizeRecording([1,2],CancellationToken.None));
        Assert.Contains(expected,error.Message);Assert.DoesNotContain("SECRET",error.Message);
    }
    [Theory]
    [InlineData("Decoded audio exceeds the ten-minute limit.","local API duration limit")]
    [InlineData("The file contains no audio track.","no audio")]
    [InlineData("The file contains no audio samples.","no audio")]
    [InlineData("The file contains no audio track. SECRET", "supported WAV")]
    public async Task OnlyExactKnownNormalizationErrorsAreRecognized(string serverMessage,string expected)
    {
        using var api=new ApiClient(new Settings(),new Handler(r=>new(HttpStatusCode.BadRequest){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new{error=new{message=serverMessage}}))}));
        var error=await Assert.ThrowsAsync<AudioImportException>(()=>api.NormalizeRecording([1,2],CancellationToken.None));Assert.Contains(expected,error.Message);Assert.DoesNotContain("SECRET",error.Message);
    }
    [Fact] public async Task MalformedFailureEnvelopeDoesNotEscapeAsJsonOrServerText()
    {
        using var api=new ApiClient(new Settings(),new Handler(r=>new(HttpStatusCode.BadRequest){Content=new StringContent("SECRET invalid JSON")}));
        var error=await Assert.ThrowsAsync<AudioImportException>(()=>api.NormalizeRecording([1,2],CancellationToken.None));Assert.Contains("supported WAV",error.Message);Assert.DoesNotContain("SECRET",error.Message);
    }
    sealed class CountedStream(byte[] data):MemoryStream(data)
    {
        public int BytesRead{get;private set;}
        public override bool CanSeek=>false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){var count=await base.ReadAsync(buffer,token);BytesRead+=count;return count;}
    }
    [Fact] public async Task ErrorEnvelopeReadIsBoundedEvenWithoutContentLength()
    {
        using var stream=new CountedStream(System.Text.Encoding.UTF8.GetBytes(new string('x',100000)));
        using var api=new ApiClient(new Settings(),new Handler(r=>new(HttpStatusCode.BadRequest){Content=new StreamContent(stream)}));
        var error=await Assert.ThrowsAsync<AudioImportException>(()=>api.NormalizeRecording([1,2],CancellationToken.None));Assert.Contains("supported WAV",error.Message);Assert.InRange(stream.BytesRead,1,4097);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task TransportFailureIsSafeAndCancellationKeepsCallerOwnership(bool callerCancelled)
    {
        using var cancel=new CancellationTokenSource();
        using var api=new ApiClient(new Settings(),new Handler(r=>{if(callerCancelled)cancel.Cancel();throw new TaskCanceledException("SECRET body",null,cancel.Token);}));
        if(callerCancelled)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>api.NormalizeRecording([1,2],cancel.Token));
        else{var error=await Assert.ThrowsAsync<AudioImportException>(()=>api.NormalizeRecording([1,2],cancel.Token));Assert.Contains("did not respond in time",error.Message);Assert.DoesNotContain("SECRET",error.Message);}
    }
    sealed class StalledStream:Stream
    {
        public override bool CanRead=>true;
        public override bool CanSeek=>false;
        public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){await Task.Delay(Timeout.Infinite,token);return 0;}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override void Flush()=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task NormalizationTimeoutAlsoBoundsStalledResponseBodies(bool errorEnvelope)
    {
        using var api=new ApiClient(new Settings(),new Handler(r=>
        {
            var content=new StreamContent(new StalledStream());content.Headers.ContentType=new(errorEnvelope?"application/json":"audio/wav");
            return new(errorEnvelope?HttpStatusCode.BadRequest:HttpStatusCode.OK){Content=content};
        }),TimeSpan.FromMilliseconds(50));
        using var cancel=new CancellationTokenSource();
        try
        {
            var error=await Assert.ThrowsAsync<AudioImportException>(()=>api.NormalizeRecording([1,2],cancel.Token).WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("did not respond in time",error.Message);
        }
        finally{cancel.Cancel();}
    }
}
