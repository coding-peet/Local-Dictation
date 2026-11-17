using System.Text.Json;
using LocalDictation.Core;
using Xunit;

public class ProtocolTests
{
    [Fact] public void BackendSnakeCaseCapabilitiesDeserialize()
    {
        var model=JsonSerializer.Deserialize<ModelInfo>(
            """{"id":"another/model","runtime_tested":true,"verification_eligible":true,"live_supported":false,"quality_verified":false}""",
            Protocol.Json)!;
        Assert.True(model.RuntimeTested);
        Assert.True(model.VerificationEligible);
        Assert.False(model.LiveSupported);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("http://127.0.0.1:48125/path")]
    [InlineData("http://user:password@localhost:48125")]
    [InlineData("https://localhost:48125")]
    public void AudioEndpointIsLoopbackOnly(string endpoint)
    {
        var settings=new Settings{Endpoint=endpoint};
        Assert.Throws<ArgumentException>(()=>settings.ServerUri());
    }

    [Fact] public void CursorGuardInvalidationIsPermanent()
    {
        var guard=new TargetGuard("a","prefix",6);
        guard.Invalidate();
        Assert.False(guard.CanReplace("a","prefix",6,6));
    }
}
