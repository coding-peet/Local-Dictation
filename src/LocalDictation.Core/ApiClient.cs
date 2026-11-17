using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalDictation.Core;

public sealed record ModelInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Revision { get; init; } = "";
    public bool Installed { get; init; }
    public bool CanInstall {get;init;}
    public bool CanRemove {get;init;}
    public string? RemovalRestriction {get;init;}
    public bool Compatible { get; init; }
    public bool RuntimeTested { get; init; }
    public bool QualityVerified { get; init; }
    public bool LiveSupported { get; init; }
    public bool LiveReady { get; init; }
    public bool VerificationEligible { get; init; }
    public string[] Languages { get; init; } = [];
    [JsonIgnore] public string Label => (Name.Length>0?Name:Id) + (!Installed ? " · not installed" : "");
    public override string ToString()=>Label;
}

public sealed record ModelRemovalResult
{
    public string ModelId {get;init;}="";
    public bool Removed {get;init;}
    public bool CleanupPending {get;init;}
    public string Message {get;init;}="";
}

public static class Protocol
{
    public static object SessionStart(Settings settings)=>new
    {
        type="start",protocol_version=1,model=settings.PrimaryModel,language=settings.Language,
        // Empty tags restore unrestricted Auto; the API rejects an empty allowlist.
        preferred_languages=settings.PreferredLanguages.Count>0?settings.PreferredLanguages:null,
        prompt=settings.VocabularyPrompt,live=settings.Live,verification_model=settings.VerifyAfterRecording?settings.VerificationModel:null
    };
    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class ApiClient : IDisposable
{
    readonly HttpClient http;
    public ApiClient(Settings settings):this(settings,new HttpClientHandler {UseProxy=false}){}
    public ApiClient(Settings settings,HttpMessageHandler handler):this(settings,handler,TimeSpan.FromSeconds(45)){}
    public ApiClient(Settings settings,TimeSpan timeout):this(settings,new HttpClientHandler {UseProxy=false},timeout){}
    public ApiClient(Settings settings,HttpMessageHandler handler,TimeSpan timeout)
    {
        http = new(handler) { BaseAddress = settings.ServerUri(), Timeout = timeout };
    }
    public async Task<JsonElement> Get(string path, CancellationToken token = default)
    {
        using var response = await http.GetAsync(path, token);
        return await Read(response, token);
    }
    public async Task<JsonElement> Post(string path, object payload, CancellationToken token = default)
    {
        using var response = await http.PostAsJsonAsync(path, payload, Protocol.Json, token);
        return await Read(response, token);
    }
    static async Task<JsonElement> Read(HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);
        using var json = JsonDocument.Parse(body);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(json.RootElement.TryGetProperty("error", out var error)
                ? error.GetProperty("message").GetString() : "The API request failed.");
        return json.RootElement.Clone();
    }
    public async Task<List<ModelInfo>> Models(CancellationToken token = default) =>
        (await Get("/v1/models", token)).GetProperty("data").Deserialize<List<ModelInfo>>(Protocol.Json) ?? [];

    public async Task<ModelRemovalResult> RemoveModel(string modelId,CancellationToken token=default)
    {
        var result=(await Post("/v1/models/remove",new {model_id=modelId},token))
            .Deserialize<ModelRemovalResult>(Protocol.Json);
        if(result is null||!result.Removed||result.ModelId!=modelId)
            throw new InvalidDataException("The API did not confirm removal of the selected model.");
        return result;
    }

    public async Task WaitForIdle(CancellationToken token,TimeSpan? timeout=null,TimeSpan? pollDelay=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout??TimeSpan.FromSeconds(30));
        try
        {
            while(true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var health=await Get("/health",deadline.Token);
                if(health.TryGetProperty("ready",out var ready)&&ready.ValueKind==JsonValueKind.True&&
                    health.TryGetProperty("busy",out var busy)&&busy.ValueKind==JsonValueKind.False)return;
                await Task.Delay(pollDelay??TimeSpan.FromMilliseconds(200),deadline.Token);
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested)
        {throw new TimeoutException("The previous transcription is still stopping. The original recording is preserved in History.");}
    }

    public async Task TestModel(ModelInfo model, string file, string language, CancellationToken token)
    {
        using var content = new MultipartFormDataContent();
        using var stream = File.OpenRead(file);
        content.Add(new StreamContent(stream), "file", Path.GetFileName(file));
        content.Add(new StringContent(model.Id), "model");
        content.Add(new StringContent(language), "language");
        using var response = await http.PostAsync("/v1/models/test", content, token);
        await Read(response, token);
    }
    public async Task<byte[]> NormalizeRecording(byte[] encoded,CancellationToken token)
    {
        AudioImportErrors.CheckEncodedLength(encoded.Length);
        var callerToken=token;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        // ResponseHeadersRead ends HttpClient's timeout at the headers. Keep the
        // configured deadline active while reading audio or the bounded error body.
        if(http.Timeout!=Timeout.InfiniteTimeSpan)deadline.CancelAfter(http.Timeout);
        token=deadline.Token;
        try
        {
            using var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(encoded),"file","recording.audio");
            using var request=new HttpRequestMessage(HttpMethod.Post,"/v1/audio/normalizations"){Content=content};
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound)throw new AudioImportException("This local API needs the audio-import update. Standard 16 kHz mono PCM16 WAV files can still be imported.");
            if(!response.IsSuccessStatusCode)
            {
                var message=(int)response.StatusCode switch
                {
                    400 or 415 or 422=>await KnownNormalizationFailure(response,token)??AudioImportErrors.UnsupportedAudio,
                    413=>AudioImportErrors.OversizeFile,
                    409 or 429=>AudioImportErrors.ApiBusy,
                    408 or 504=>AudioImportErrors.ApiTimeout,
                    401 or 403=>AudioImportErrors.ApiRejected,
                    502 or 503=>AudioImportErrors.ApiUnavailable,
                    _=>AudioImportErrors.ApiProcessingFailed
                };
                throw new AudioImportException(message);
            }
            const int limit=600*32000+44;
            if(response.Content.Headers.ContentType?.MediaType!="audio/wav")throw new AudioImportException(AudioImportErrors.InvalidResponse);
            if(response.Content.Headers.ContentLength>limit)throw new AudioImportException(AudioImportErrors.DurationLimit);
            await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[65536];
            try
            {
                int read;while((read=await input.ReadAsync(buffer,token))>0)
                {if(output.Length+read>limit)throw new AudioImportException(AudioImportErrors.DurationLimit);output.Write(buffer,0,read);}
                return output.ToArray();
            }
            finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);if(output.TryGetBuffer(out var bytes))System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes.AsSpan());}
        }
        catch(OperationCanceledException) when(!callerToken.IsCancellationRequested){throw new AudioImportException(AudioImportErrors.ApiTimeout);}
        catch(TimeoutException){throw new AudioImportException(AudioImportErrors.ApiTimeout);}
        catch(Exception error) when(error is HttpRequestException or IOException){throw new AudioImportException(AudioImportErrors.ApiUnavailable);}
    }
    static async Task<string?> KnownNormalizationFailure(HttpResponseMessage response,CancellationToken token)
    {
        const int limit=4096;
        if(response.Content.Headers.ContentLength>limit)return null;
        var buffer=new byte[limit+1];
        try
        {
            await using var input=await response.Content.ReadAsStreamAsync(token);var count=0;
            while(count<buffer.Length)
            {
                var read=await input.ReadAsync(buffer.AsMemory(count,buffer.Length-count),token);
                if(read==0)break;count+=read;
            }
            if(count>limit)return null;
            using var json=JsonDocument.Parse(buffer.AsMemory(0,count),new JsonDocumentOptions{MaxDepth=8});
            if(json.RootElement.ValueKind!=JsonValueKind.Object||!json.RootElement.TryGetProperty("error",out var error)||error.ValueKind!=JsonValueKind.Object||
                !error.TryGetProperty("message",out var message)||message.ValueKind!=JsonValueKind.String)return null;
            return message.GetString() switch
            {
                "Decoded audio exceeds the ten-minute limit."=>AudioImportErrors.DurationLimit,
                "The file contains no audio track." or "The file contains no audio samples."=>AudioImportErrors.NoAudio,
                _=>null
            };
        }
        catch(JsonException){return null;}
        finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);}
    }
    public async Task<SavedVerificationResult> VerifyRecording(byte[] wave,ReviewDocument source,string verifier,string language,
        IReadOnlyList<string> preferred,CancellationToken token)
    {
        using var content=new MultipartFormDataContent();
        var audio=new ByteArrayContent(wave);audio.Headers.ContentType=new("audio/wav");
        content.Add(audio,"file","recording.wav");content.Add(new StringContent(verifier),"model");
        content.Add(new StringContent(source.Primary),"primary");content.Add(new StringContent(source.PrimaryModel),"primary_model");
        content.Add(new StringContent(language),"language");
        if(source.Provenance?.Primary.Revision is {} revision)content.Add(new StringContent(revision),"primary_revision");
        if(preferred.Count>0)content.Add(new StringContent(JsonSerializer.Serialize(preferred)),"preferred_languages");
        using var response=await http.PostAsync("/v1/audio/verifications",content,token);
        return (await Read(response,token)).Deserialize<SavedVerificationResult>(Protocol.Json)??throw new InvalidDataException("No second transcript was returned.");
    }
    public void Dispose() => http.Dispose();
}

public sealed class SessionStream : IAsyncDisposable
{
    readonly ClientWebSocket socket = new();
    readonly SemaphoreSlim sendGate = new(1);
    string? session;
    int revision;
    SessionDeadline? deadline;
    public async Task Connect(Settings settings, CancellationToken token)
    {
        socket.Options.Proxy=null;
        var clock=Stopwatch.GetTimestamp();
        deadline=new SessionDeadline(()=>Stopwatch.GetElapsedTime(clock),TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(600),TimeSpan.FromMinutes(10));
        var uri = new UriBuilder(settings.ServerUri()) { Scheme = "ws", Path = "/v1/audio/sessions" }.Uri;
        await socket.ConnectAsync(uri, token);
        await Send(Protocol.SessionStart(settings),token);
    }
    public async Task Finish(CancellationToken token)
    {
        deadline?.Finish();
        await Send(new {type="finish"},token);
    }
    public async Task Send(object data, CancellationToken token) =>
        await SendBytes(JsonSerializer.SerializeToUtf8Bytes(data, Protocol.Json), WebSocketMessageType.Text, token);
    public Task SendAudio(byte[] pcm, CancellationToken token) =>
        SendBytes(pcm, WebSocketMessageType.Binary, token);
    async Task SendBytes(byte[] bytes, WebSocketMessageType type, CancellationToken token)
    {
        await sendGate.WaitAsync(token);
        try { await socket.SendAsync(bytes.AsMemory(), type, true, token); }
        finally { sendGate.Release(); }
    }
    public async Task Receive(Func<JsonElement, Task> handle, CancellationToken token)
    {
        var buffer = new byte[65536];
        using var message = new MemoryStream();
        while (!token.IsCancellationRequested && socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), token).AsTask().WaitAsync(
                deadline?.Remaining()??TimeSpan.FromSeconds(10),token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Unexpected API frame.");
            message.Write(buffer, 0, result.Count);
            if (message.Length > 2 * 1024 * 1024) throw new InvalidDataException("API message too large.");
            if (!result.EndOfMessage) continue;
            using var document = JsonDocument.Parse(message.GetBuffer().AsMemory(0, (int)message.Length));
            message.SetLength(0);
            var e = document.RootElement.Clone();
            if (e.TryGetProperty("session_id", out var id))
            {
                session ??= id.GetString();
                var next = e.GetProperty("revision").GetInt32();
                if (id.GetString() != session || next <= revision)
                    throw new InvalidDataException("Stale or foreign session revision.");
                revision = next;
            }
            deadline?.Observe(e.GetProperty("type").GetString()??"");
            await handle(e);
            if (e.GetProperty("type").GetString() is "session.completed" or "session.cancelled" or "session.error") break;
        }
    }
    public async ValueTask DisposeAsync()
    {
        socket.Abort();
        socket.Dispose();
        sendGate.Dispose();
        await ValueTask.CompletedTask;
    }
}
