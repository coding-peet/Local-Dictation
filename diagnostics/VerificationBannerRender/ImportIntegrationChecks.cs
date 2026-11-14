using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.Core;

public static class ImportIntegrationChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object x,string n,object? v)=>x.GetType().GetField(n,Fields)!.SetValue(x,v);
    static T Get<T>(object x,string n)=>(T)x.GetType().GetField(n,Fields)!.GetValue(x)!;
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static void Pump(Task task)
    {
        var frame=new DispatcherFrame();var start=DateTime.UtcNow;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
        timer.Tick+=(_,_)=>{if(task.IsCompleted||DateTime.UtcNow-start>TimeSpan.FromSeconds(30)){timer.Stop();frame.Continue=false;}};
        timer.Start();Dispatcher.PushFrame(frame);Require(task.IsCompleted,"Synthetic import did not finish within its deadline.");task.GetAwaiter().GetResult();
    }
    static byte[] Wave(int rate=16000,int channels=1,short sample=4096,int seconds=1)
    {
        var data=new byte[44+rate*channels*2*seconds];"RIFF"u8.CopyTo(data);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4),data.Length-8);"WAVEfmt "u8.CopyTo(data.AsSpan(8));BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16),16);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(20),1);BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22),(short)channels);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24),rate);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(28),rate*channels*2);BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(32),(short)(channels*2));BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(34),16);"data"u8.CopyTo(data.AsSpan(36));BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(40),data.Length-44);
        for(var i=44;i<data.Length;i+=2)BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i),sample);return data;
    }
    public static void Run(string output,List<string> checks)
    {
        var endpoint=Environment.GetEnvironmentVariable("LOCAL_DICTATION_SYNTHETIC_IMPORT_ENDPOINT");
        Require(endpoint=="http://127.0.0.1:48226","This check only uses the explicitly selected synthetic fixture on 48226.");
        var settings=new Settings{Endpoint=endpoint!,PrimaryModel="kinit/whisper-large-v3-turbo-sk",VerificationModel="openai/whisper-large-v3",VerifyAfterRecording=true,Language="sk",PreferredLanguages=["sk"]};
        using var api=new ApiClient(settings);var healthTask=api.Get("/health");Pump(healthTask);Require(healthTask.Result.GetProperty("runtime").GetProperty("synthetic_fixture").GetBoolean(),"Refusing a non-synthetic API.");
        var root=Path.Combine(output,"integration");Directory.CreateDirectory(root);
        var paths=new[]{Path.Combine(root,"ordinary.wav"),Path.Combine(root,"too-long.wav"),Path.Combine(root,"broken.wav"),Path.Combine(root,"stereo.wav")};
        File.WriteAllBytes(paths[0],Wave());File.WriteAllBytes(paths[1],Wave(seconds:2));File.WriteAllBytes(paths[2],[1,2,3]);File.WriteAllBytes(paths[3],Wave(48000,2));var original=paths.Select(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        var owner=ReviewWorkflowChecks.Controller(output,new ReviewDocument());Set(owner,"<Settings>k__BackingField",settings);var modelsTask=api.Models();Pump(modelsTask);Set(owner,"<Models>k__BackingField",modelsTask.Result);Set(owner,"<Connected>k__BackingField",true);
        Set(owner,"historyStore",new HistoryStore(Path.Combine(root,"history")));Set(owner,"<History>k__BackingField",new List<HistoryEntry>());Set(owner,"pending",new PendingReviews(new ReviewStore(Path.Combine(root,"pending"))));Set(owner,"recordingTasks",new HashSet<Task>());
        Require(owner.StartAudioImport(paths)==null,"Synthetic batch was refused.");Pump(Task.WhenAll(Get<HashSet<Task>>(owner,"recordingTasks").ToArray()));
        Require(owner.ImportItems.Select(i=>i.State).SequenceEqual(new[]{"Completed","Failed","Failed","Completed"}),"Actual transport did not preserve per-file failure/continuation.");
        Require(owner.History.Count==3&&owner.History.All(h=>h.Document.RequiresExplicitInsert&&h.Document.InsertedAt==null)&&owner.History.Count(h=>h.Document.HasCompletedVerification&&h.Document.Provenance?.Primary.Revision!=null)==2&&owner.History.Single(h=>h.Document.ImportedAudio?.FileName=="too-long.wav").DurationSeconds==2,"Imports lost protected History, explicit placement or actual verification provenance.");
        Require(Get<Dictionary<string,ITextDelivery>>(owner,"destinations").Count==0&&owner.BannerReview==null&&!owner.ShowBanner,"Batch captured a cursor or opened completion UI.");
        Require(paths.Select(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).SequenceEqual(original),"Import changed an original source.");
        var blocked=Path.Combine(root,"cancel.wav");File.WriteAllBytes(blocked,Wave(sample:8192));
        Require(owner.StartAudioImport([blocked,paths[0]])==null,"Cancellation batch refused.");
        var poll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};var polling=false;
        poll.Tick+=async(_,_)=>{if(polling)return;polling=true;try{var h=await api.Get("/health");if(h.GetProperty("runtime").GetProperty("blocking").GetBoolean()){poll.Stop();owner.CancelAudioImport();}}finally{polling=false;}};
        poll.Start();try{Pump(Task.WhenAll(Get<HashSet<Task>>(owner,"recordingTasks").ToArray()));}finally{poll.Stop();}
        Require(owner.ImportItems.All(i=>i.State=="Cancelled")&&!owner.IsImporting&&owner.History.Count==4,"Cancelling actual inference lost earlier History or ran a remaining file.");
        var idle=api.Get("/health");Pump(idle);Require(!idle.Result.GetProperty("busy").GetBoolean()&&idle.Result.GetProperty("runtime").GetProperty("cancelled_count").GetInt32()>0,"Import released its client guard before the actual worker stopped.");
        Require(Get<Dictionary<string,ITextDelivery>>(owner,"destinations").Count==0,"Cancellation acquired a native destination.");
        checks.Add("Synthetic actual HTTP normalization + WebSocket primary/verification, lower-server-limit rejection/exact-cap acceptance, protected History, failed-file continuation, cancellation/idle drain and unchanged sources with zero native destination");
    }
}
