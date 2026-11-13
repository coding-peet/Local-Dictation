using System.Net;
using System.Text;
using LocalDictation.Core;
using Xunit;

public sealed class SavedVerificationClientTests
{
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>action(request,token);}
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostsOriginalAudioAndExactPrimaryWithoutExportingOrChangingIt(bool constrained)
    {
        var wave=Encoding.ASCII.GetBytes("synthetic original WAV bytes");
        var source=new ReviewDocument{PrimaryModel="first",ManualFinalDraft="human correction"};source.SetPrimary("original words");
        source.AttachProvenance(new("session",new("first","pinned-source"),null,null));
        var fields=new Dictionary<string,string>();byte[]? received=null;
        var handler=new Handler(async (request,token)=>
        {
            Assert.Equal("/v1/audio/verifications",request.RequestUri!.AbsolutePath);Assert.Equal(HttpMethod.Post,request.Method);
            foreach(var part in (MultipartFormDataContent)request.Content!)
            {
                var name=part.Headers.ContentDisposition!.Name!.Trim('"');
                if(name=="file")received=await part.ReadAsByteArrayAsync(token);else fields[name]=await part.ReadAsStringAsync(token);
            }
            return new(HttpStatusCode.OK){Content=new StringContent("""
                {"primary":"original words","verification":"second words","pieces":[{"id":"0123456789abcdef","primary":"original words","verification":"second words","different":true}],"primary_model":"first","primary_revision":"pinned-source","verification_model":"second","verification_revision":"actual-second","comparison_id":"compare","language":"en","language_mode":"forced","duration":159}
                """)};
        });
        using var client=new ApiClient(new Settings(),handler,TimeSpan.FromMinutes(11));
        var result=await client.VerifyRecording(wave,source,"second","en",constrained?["en"]:[],CancellationToken.None);
        Assert.Equal(wave,received);Assert.Equal("original words",fields["primary"]);Assert.Equal("pinned-source",fields["primary_revision"]);
        Assert.Equal("second",fields["model"]);Assert.Equal(constrained,fields.ContainsKey("preferred_languages"));
        Assert.Equal("human correction",source.ManualFinalDraft);Assert.Equal("original words",source.Primary);
        Assert.Equal("actual-second",result.VerificationRevision);Assert.Equal(159,result.Duration);
    }
}
