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
}
