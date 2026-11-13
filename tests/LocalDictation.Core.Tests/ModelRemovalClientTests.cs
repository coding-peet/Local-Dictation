using System.Net;
using System.Text.Json;
using LocalDictation.Core;
using Xunit;

public sealed class ModelRemovalClientTests
{
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>action(request,token);}

    [Fact]
    public async Task SendsExactModelIdentityInExplicitPostAndReadsCleanupStatus()
    {
        var handler=new Handler(async (request,token)=>
        {
            Assert.Equal(HttpMethod.Post,request.Method);
            Assert.Equal("/v1/models/remove",request.RequestUri!.AbsolutePath);
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("owner/whisper-tiny",body.RootElement.GetProperty("model_id").GetString());
            Assert.Single(body.RootElement.EnumerateObject());
            return new(HttpStatusCode.OK){Content=new StringContent("""
                {"model_id":"owner/whisper-tiny","removed":true,"cleanup_pending":true,"message":"Model removed. Recovery files remain."}
                """)};
        });
        using var client=new ApiClient(new Settings(),handler);
        var result=await client.RemoveModel("owner/whisper-tiny");
        Assert.True(result.Removed);Assert.True(result.CleanupPending);
        Assert.Equal("Model removed. Recovery files remain.",result.Message);
    }

    [Fact]
    public async Task PropagatesRemovalRestrictionFromBackend()
    {
        var handler=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {Content=new StringContent("""{"error":{"message":"A model download is running."}}""")}));
        using var client=new ApiClient(new Settings(),handler);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>client.RemoveModel("owner/model"));
        Assert.Equal("A model download is running.",error.Message);
    }

    [Theory]
    [InlineData("other/model",true)]
    [InlineData("owner/model",false)]
    public async Task RejectsUnconfirmedOrForeignRemoval(string returned,bool removed)
    {
        var body=JsonSerializer.Serialize(new {model_id=returned,removed});
        var handler=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)}));
        using var client=new ApiClient(new Settings(),handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.RemoveModel("owner/model"));
    }

    [Fact]
    public void DeserializesManagedAndSharedRemovalAvailabilityWithoutGuessing()
    {
        var shared=JsonSerializer.Deserialize<ModelInfo>("""
            {"id":"shared/model","installed":true,"can_remove":false,"removal_restriction":"Shared files are preserved."}
            """,Protocol.Json)!;
        Assert.False(shared.CanRemove);Assert.Equal("Shared files are preserved.",shared.RemovalRestriction);
        var managed=JsonSerializer.Deserialize<ModelInfo>("""
            {"id":"owned/model","installed":true,"can_remove":true,"removal_restriction":null}
            """,Protocol.Json)!;
        Assert.True(managed.CanRemove);Assert.Null(managed.RemovalRestriction);
    }
}
