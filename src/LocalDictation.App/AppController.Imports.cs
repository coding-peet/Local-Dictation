using System.Security.Cryptography;
using System.Text.Json;
using LocalDictation.Core;
using LocalDictation.App.Services;

namespace LocalDictation.App;

public sealed partial class AppController
{
    AudioImportQueue? audioImportQueue;
    CancellationTokenSource? importCancel;
    bool importCleanupPending;
    string? importCleanupEndpoint;
    public bool IsImporting=>importCancel!=null||importCleanupPending;
    public IReadOnlyList<AudioImportItem> ImportItems=>audioImportQueue?.Items??[];
    public string ImportSummary=>importCleanupPending?"Stopping: waiting for the local API to release the import.":audioImportQueue==null?"":
        $"{ImportItems.Count(i=>i.State=="Completed")} / {ImportItems.Count} completed"+(importCancel?.IsCancellationRequested==true?" · Cancelling":"");
    public string AudioImportSettingsSummary=>$"{Models.FirstOrDefault(m=>m.Id==Settings.PrimaryModel)?.Name??Settings.PrimaryModel} · {Settings.Language.ToUpperInvariant()} · "+
        (Settings.VerifyAfterRecording?$"Verify: {Models.FirstOrDefault(m=>m.Id==Settings.VerificationModel)?.Name??Settings.VerificationModel}":"No verification")+" · Saved to History";
    public string? StartAudioImport(IReadOnlyList<string> paths)
    {
        if(paths.Count==0)return "Select audio files first.";
        if(IsBusy||IsApplyingReview||inserting.Count>0||deliveryBusy||ClipboardRecovery.HasPending||exiting)return "Finish the current operation before importing audio.";
        if(PrivateNextRecording)return "Imports are saved to History. Turn off Private for next microphone recording before importing.";
        var settings=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Settings))!;
        var model=Models.FirstOrDefault(m=>m.Id==settings.PrimaryModel&&m.Installed);
        if(!Connected||model==null)return "Connect to the local API and select an installed dictation model first.";
        try{LanguagePolicy.ValidateForModel(model,settings.Language,settings.PreferredLanguages);}
        catch(ArgumentException error){return error.Message;}
        if(settings.VerifyAfterRecording&&!Models.Any(m=>m.Id==settings.VerificationModel&&m.Id!=model.Id&&ModelEligibility.CanVerify(m,settings.Language,model,settings.PreferredLanguages)))return "Choose a compatible second model first.";
        settings.Live=false;settings.InsertAtCursor=false;settings.AfterRecording=AfterRecordingAction.PreviewFirst;
        importCancel=new();audioImportQueue=new(paths,()=>new ReviewDocument{PrimaryModel=settings.PrimaryModel,RequestedLanguage=settings.Language,
            VerificationModel=settings.VerifyAfterRecording?settings.VerificationModel:null,RequiresExplicitInsert=true});
        audioImportQueue.Changed+=ImportChanged;
        TrackRecording(RunAudioImport(paths.ToArray(),settings,importCancel));Changed?.Invoke();return null;
    }
    void ImportChanged()=>Changed?.Invoke();
    public void CancelAudioImport(){importCancel?.Cancel();Changed?.Invoke();}
    async Task RunAudioImport(string[] paths,Settings settings,CancellationTokenSource cancel)
    {
        var queue=audioImportQueue!;var snapshots=new Dictionary<string,AudioImportSource>();var invalid=new HashSet<string>();
        try
        {
            // Freeze every source identity before the first transcription. Only
            // one bounded encoded file is held while decoding; originals stay untouched.
            foreach(var path in paths.Distinct())
            {
                if(cancel.IsCancellationRequested)break;
                try{snapshots[path]=await AudioImportSource.CaptureAsync(path,cancel.Token);}
                catch(OperationCanceledException) when(cancel.IsCancellationRequested){break;}
                catch(Exception){invalid.Add(path);}
            }
            await queue.RunAsync(async (path,token)=>
            {
                if(invalid.Contains(path)||!snapshots.TryGetValue(path,out var source))throw new IOException("The selected file is unavailable or exceeds the import limit.");
                var encoded=await source.ReadAsync(token);byte[]? normalized=null;
                try
                {
                    byte[] pcm;
                    try{pcm=RecordedAudio.PcmFromWave(encoded);}
                    catch(InvalidDataException)
                    {
                        using var api=new ApiClient(settings,TimeSpan.FromMinutes(11));await api.WaitForIdle(token);
                        try{normalized=await api.NormalizeRecording(encoded,token);}
                        finally{await DrainImportApi(api,settings);}
                        pcm=RecordedAudio.PcmFromWave(normalized);
                    }
                    return new PreparedAudioImport(source.FileName,source.Sha256,pcm);
                }
                finally{CryptographicOperations.ZeroMemory(encoded);if(normalized!=null)CryptographicOperations.ZeroMemory(normalized);}
            },(audio,document,token)=>TranscribeImportedAudio(audio,document,settings,token),SaveImportedAudio,cancel.Token);
        }
        catch(Exception error){Diagnostics.Error("audio import",error);ConnectionMessage="Audio import stopped. Completed results are kept in History.";}
        finally
        {
            queue.Changed-=ImportChanged;importCancel=null;cancel.Dispose();
            if(importCleanupPending)ConnectionMessage="Audio import cleanup could not be confirmed. Keep the local API running until it reports idle.";
            Changed?.Invoke();
        }
    }
    async Task<bool> TranscribeImportedAudio(PreparedAudioImport audio,ReviewDocument document,Settings settings,CancellationToken token)
    {
        using var api=new ApiClient(settings,TimeSpan.FromMinutes(11));
        var stream=new SessionStream();Task? receive=null;
        try
        {
            await api.WaitForIdle(token);
            var health=await api.Get("/health",token);var maxSeconds=health.GetProperty("max_seconds").GetInt32();
            ImportedTranscript.ValidateDuration(audio.Pcm.Length,maxSeconds);
            var result=new ImportedTranscript(document,audio.Pcm.Length,maxSeconds*32000L);
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await stream.Connect(settings,token);
            receive=stream.Receive(e=>{result.Observe(e);if(e.GetProperty("type").GetString()=="session.started")ready.TrySetResult();return Task.CompletedTask;},token);
            if(await Task.WhenAny(ready.Task,receive)==receive){await receive;if(!ready.Task.IsCompleted)throw new InvalidDataException("The import session did not start.");}
            await ready.Task.WaitAsync(token);
            for(var offset=0;offset<audio.Pcm.Length;offset+=64000)
            {
                token.ThrowIfCancellationRequested();var frame=audio.Pcm.AsSpan(offset,Math.Min(64000,audio.Pcm.Length-offset)).ToArray();
                try{await stream.SendAudio(frame,token);}finally{CryptographicOperations.ZeroMemory(frame);}
            }
            if(!receive.IsCompleted)
            {
                try{await stream.Finish(token);}
                catch(Exception) when(receive.IsCompletedSuccessfully&&result.Completed){}
            }
            await receive;return result.Completed;
        }
        finally
        {
            await stream.DisposeAsync();
            if(receive!=null)try{await receive;}catch(Exception){}
            await DrainImportApi(api,settings);
        }
    }
    async Task DrainImportApi(ApiClient api,Settings settings)
    {
        try{await api.WaitForIdle(CancellationToken.None,TimeSpan.FromMinutes(11));}
        catch(Exception){importCleanupPending=true;importCleanupEndpoint=settings.Endpoint;importCancel?.Cancel();throw;}
    }
    Task SaveImportedAudio(PreparedAudioImport audio,ReviewDocument document)
    {
        try{historyStore.Add(document,audio.Pcm);History=historyStore.Load();}
        catch(Exception){SaveReview(document);ConnectionMessage="Imported text is in Review; its recording could not be saved.";throw;}
        SaveReview(document);Changed?.Invoke();
        if(pending.UnsavedIds.Contains(document.Id))throw new IOException("Protected saving failed; text remains in memory.");
        return Task.CompletedTask;
    }
}
