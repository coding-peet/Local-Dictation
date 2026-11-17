using System.Text.Json;
using LocalDictation.Core;
using Xunit;

public class AudioImportTests
{
    [Fact] public async Task QueueIsSerialSavesEarlierResultsAndCancelsRemaining()
    {
        using var cancel=new CancellationTokenSource();var order=new List<string>();
        var queue=new AudioImportQueue(["one.wav","two.wav","three.wav"]);
        await queue.RunAsync((path,token)=>Task.FromResult(new PreparedAudioImport(Path.GetFileName(path),"hash",new byte[32000])),
            (audio,document,token)=>{order.Add(audio.FileName);document.SetPrimary("received "+audio.FileName);if(audio.FileName=="two.wav")cancel.Cancel();return Task.FromResult(audio.FileName!="two.wav");},
            (audio,document)=>{order.Add("saved "+document.OutputText);return Task.CompletedTask;},cancel.Token);
        Assert.Equal(new[]{"one.wav","saved received one.wav","two.wav","saved received two.wav"},order);
        Assert.Equal(new[]{"Completed","Cancelled","Cancelled"},queue.Items.Select(i=>i.State));
        Assert.All(queue.Documents,d=>Assert.True(d.RequiresExplicitInsert));
    }
    [Fact] public async Task FailedItemPreservesDecodedPartialAndNextFileCanContinue()
    {
        var queue=new AudioImportQueue(["one","two"]);var saved=new List<ReviewDocument>();
        await queue.RunAsync((path,token)=>Task.FromResult(new PreparedAudioImport(path,"hash",new byte[32000])),
            (audio,document,token)=>{document.SetPrimary(audio.FileName);if(audio.FileName=="one")throw new IOException("private filesystem path");return Task.FromResult(true);},
            (audio,document)=>{saved.Add(document);return Task.CompletedTask;},CancellationToken.None);
        Assert.Equal(2,saved.Count);Assert.Equal(new[]{"Failed","Completed"},queue.Items.Select(i=>i.State));
        Assert.DoesNotContain("private filesystem",queue.Items[0].Message);
    }
    [Fact] public async Task SaveFailureDoesNotEraseCompletedEarlierItem()
    {
        var queue=new AudioImportQueue(["one","two"]);var saved=new List<string>();
        await queue.RunAsync((path,token)=>Task.FromResult(new PreparedAudioImport(path,"hash",new byte[32000])),
            (audio,d,t)=>{d.SetPrimary(audio.FileName);return Task.FromResult(true);},
            (audio,d)=>{if(audio.FileName=="two")throw new IOException();saved.Add(d.OutputText);return Task.CompletedTask;},CancellationToken.None);
        Assert.Equal(new[]{"one"},saved);Assert.Equal("Failed",queue.Items[1].State);Assert.Equal("two",queue.Documents[1].OutputText);
    }
    [Fact] public void ImportedEventsPreserveRawTextVerificationAndPinnedModelsWithoutDestination()
    {
        var d=new ReviewDocument{PrimaryModel="first",VerificationModel="second",RequiresExplicitInsert=true};
        var result=new ImportedTranscript(d);
        void Event(string json){using var e=JsonDocument.Parse(json);result.Observe(e.RootElement);}
        Event("""{"type":"session.started","session_id":"s","model":"first","model_revision":"one","verification_revision":"two"}""");
        Event("""{"type":"transcript.final","text":"original","language":"sk","language_mode":"forced"}""");
        Event("""{"type":"verification.completed","verification":"alternative","pieces":[{"id":"p","primary":"original","verification":"alternative","different":true}],"comparison_id":"comparison"}""");
        Event("""{"type":"session.completed","text":"original"}""");
        Assert.True(result.Completed);Assert.Equal("original",d.Primary);Assert.Equal("alternative",d.Verification);
        Assert.Equal("two",d.Provenance!.Verification!.Revision);Assert.Null(d.InsertedAt);Assert.True(d.RequiresExplicitInsert);
    }
    [Fact] public void EmptyFinalCannotEraseReceivedPartialOnFailure()
    {
        var d=new ReviewDocument();var transcript=new ImportedTranscript(d);
        using var first=JsonDocument.Parse("""{"type":"transcript.updated","text":"preserve me"}""");transcript.Observe(first.RootElement);
        using var last=JsonDocument.Parse("""{"type":"transcript.final","text":""}""");transcript.Observe(last.RootElement);
        Assert.Equal("preserve me",d.Primary);Assert.False(transcript.Completed);
    }
    [Theory] [InlineData(32000,true)] [InlineData(64000,false)]
    public void LimitTerminationOnlyCompletesWhenWholeExpectedAudioWasAccepted(int expected,bool complete)
    {
        var d=new ReviewDocument();var result=new ImportedTranscript(d,expected,32000);
        foreach(var json in new[]{"{\"type\":\"recording.limit_reached\",\"audio_received_seconds\":1}","{\"type\":\"transcript.final\",\"text\":\"received\"}","{\"type\":\"session.completed\"}"})
        {using var e=JsonDocument.Parse(json);result.Observe(e.RootElement);}
        Assert.Equal(complete,result.Completed);
    }
    [Theory]
    [InlineData("prepare","missing","no longer available")]
    [InlineData("prepare","access","permissions")]
    [InlineData("prepare","format","supported WAV")]
    [InlineData("prepare","http","local API is unavailable")]
    [InlineData("transcribe","http","local API is unavailable")]
    [InlineData("transcribe","socket","local API is unavailable")]
    [InlineData("transcribe","timeout","did not respond in time")]
    [InlineData("transcribe","cancel","did not respond in time")]
    [InlineData("transcribe","other","Transcription did not finish")]
    [InlineData("save","other","could not be fully saved")]
    public async Task QueueReportsSafeActionableFailureForItsPhase(string phase,string kind,string expected)
    {
        Exception Error()=>kind switch
        {
            "missing"=>new FileNotFoundException("SECRET path and transcript"),
            "access"=>new UnauthorizedAccessException("SECRET path and transcript"),
            "format"=>new InvalidDataException("SECRET path and transcript"),
            "http"=>new HttpRequestException("SECRET protocol body"),
            "socket"=>new System.Net.WebSockets.WebSocketException("SECRET protocol body"),
            "timeout"=>new TimeoutException("SECRET body"),
            "cancel"=>new TaskCanceledException("SECRET body"),
            _=>new IOException("SECRET path and transcript")
        };
        var queue=new AudioImportQueue(["synthetic.wav"]);
        await queue.RunAsync((path,t)=>phase=="prepare"?Task.FromException<PreparedAudioImport>(Error()):Task.FromResult(new PreparedAudioImport(path,"hash",new byte[2])),
            (audio,d,t)=>phase=="transcribe"?Task.FromException<bool>(Error()):Task.FromResult(true),
            (audio,d)=>phase=="save"?Task.FromException(Error()):Task.CompletedTask,CancellationToken.None);
        Assert.Equal("Failed",queue.Items[0].State);Assert.Contains(expected,queue.Items[0].Message);Assert.DoesNotContain("SECRET",queue.Items[0].Message);
    }
    [Fact] public async Task TranscriptionAndSaveFailuresAreBothReportedWithPartialTextAndZeroedAudio()
    {
        var pcm=new byte[]{1,2};var queue=new AudioImportQueue(["one","two"]);var saved=new List<string>();
        await queue.RunAsync((path,t)=>Task.FromResult(new PreparedAudioImport(path,"hash",path=="one"?pcm:new byte[]{3,4})),
            (audio,d,t)=>{d.SetPrimary(audio.FileName);if(audio.FileName=="one")throw new AudioImportException("This recording exceeds the local API duration limit.");return Task.FromResult(true);},
            (audio,d)=>{if(audio.FileName=="one")throw new IOException("SECRET save path");saved.Add(d.OutputText);return Task.CompletedTask;},CancellationToken.None);
        Assert.Equal(new[]{"Failed","Completed"},queue.Items.Select(i=>i.State));Assert.Equal(new[]{"two"},saved);
        Assert.Contains("duration limit",queue.Items[0].Message);Assert.Contains("could not be fully saved",queue.Items[0].Message);Assert.Contains("Keep the app open",queue.Items[0].Message);
        Assert.DoesNotContain("SECRET",queue.Items[0].Message);Assert.Equal("one",queue.Documents[0].OutputText);Assert.Equal(new byte[]{0,0},pcm);
    }
    [Fact] public async Task CancellationDoesNotHideCriticalSaveFailure()
    {
        using var cancel=new CancellationTokenSource();var queue=new AudioImportQueue(["one","two"]);
        await queue.RunAsync((path,t)=>Task.FromResult(new PreparedAudioImport(path,"hash",new byte[]{1,2})),
            (audio,d,t)=>{d.SetPrimary("partial");cancel.Cancel();throw new OperationCanceledException(t);},
            (audio,d)=>throw new IOException("SECRET"),cancel.Token);
        Assert.Equal(new[]{"Failed","Cancelled"},queue.Items.Select(i=>i.State));Assert.Contains("could not be fully saved",queue.Items[0].Message);Assert.Equal("partial",queue.Documents.Single().OutputText);
    }
    [Fact] public async Task IncompleteTranscriptionAndSaveFailureAreBothReported()
    {
        var queue=new AudioImportQueue(["one"]);
        await queue.RunAsync((path,t)=>Task.FromResult(new PreparedAudioImport(path,"hash",new byte[2])),
            (audio,d,t)=>Task.FromResult(false),(audio,d)=>throw new IOException("SECRET"),CancellationToken.None);
        Assert.Contains("Transcription did not finish",queue.Items[0].Message);Assert.Contains("could not be fully saved",queue.Items[0].Message);
    }
    [Fact] public async Task SourceCaptureFailuresStaySpecificForEachQueuedPath()
    {
        var failures=new Dictionary<string,Exception>{{"empty",new AudioImportException("This audio file is empty. Choose a file that contains audio.")},{"missing",new FileNotFoundException("SECRET")}};
        var queue=new AudioImportQueue(["empty","missing","valid"]);
        await queue.RunAsync((path,t)=>failures.TryGetValue(path,out var failure)?Task.FromException<PreparedAudioImport>(failure):Task.FromResult(new PreparedAudioImport(path,"hash",new byte[2])),
            (audio,d,t)=>Task.FromResult(true),(audio,d)=>Task.CompletedTask,CancellationToken.None);
        Assert.Contains("empty",queue.Items[0].Message);Assert.Contains("no longer available",queue.Items[1].Message);Assert.Equal("Completed",queue.Items[2].State);
    }
}
