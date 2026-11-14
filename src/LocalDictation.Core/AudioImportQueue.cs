using System.Security.Cryptography;
namespace LocalDictation.Core;

public sealed class AudioImportException(string message):Exception(message);
public sealed record ImportedAudioSource(string FileName,string Sha256,double DurationSeconds);
public sealed record AudioImportItem(string FileName,string State="Queued",string Message="");
public sealed class PreparedAudioImport(string fileName,string sha256,byte[] pcm):IDisposable
{
    public string FileName{get;}=fileName;
    public string Sha256{get;}=sha256;
    public byte[] Pcm{get;}=pcm;
    public void Dispose()=>CryptographicOperations.ZeroMemory(Pcm);
}

/// <summary>Serial, destination-free orchestration. Prepared audio is cleared after protected saving.</summary>
public sealed class AudioImportQueue(IEnumerable<string> paths,Func<ReviewDocument>? createDocument=null)
{
    readonly string[] paths=paths.ToArray();
    readonly List<AudioImportItem> items=paths.Select(p=>new AudioImportItem(Path.GetFileName(p))).ToList();
    readonly List<ReviewDocument> documents=[];
    bool started;
    public IReadOnlyList<AudioImportItem> Items=>items;
    public IReadOnlyList<ReviewDocument> Documents=>documents;
    public event Action? Changed;
    void Update(int index,string state,string message=""){items[index]=items[index] with {State=state,Message=message};Changed?.Invoke();}
    public async Task RunAsync(Func<string,CancellationToken,Task<PreparedAudioImport>> prepare,
        Func<PreparedAudioImport,ReviewDocument,CancellationToken,Task<bool>> transcribe,
        Func<PreparedAudioImport,ReviewDocument,Task> save,CancellationToken token)
    {
        if(started)throw new InvalidOperationException("This import batch has already started.");started=true;
        for(var i=0;i<paths.Length;i++)
        {
            if(token.IsCancellationRequested){Update(i,"Cancelled");continue;}
            PreparedAudioImport? audio=null;ReviewDocument? document=null;var completed=false;var failed=false;string? failureMessage=null;
            try
            {
                Update(i,"Preparing");audio=await prepare(paths[i],token);token.ThrowIfCancellationRequested();
                document=createDocument?.Invoke()??new();document.RequiresExplicitInsert=true;
                document.ImportedAudio=new(audio.FileName,audio.Sha256,audio.Pcm.Length/32000.0);
                document.Reason="Imported audio. Open Review and choose Insert when ready.";documents.Add(document);
                Update(i,"Transcribing");completed=await transcribe(audio,document,token);
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){}
            catch(Exception error){failed=true;failureMessage=(error as AudioImportException)?.Message;}
            finally
            {
                try
                {
                    if(audio!=null&&document!=null)
                    {
                        if(!completed)document.Reason="Audio import did not finish. Received text is preserved; review before inserting.";
                        Update(i,"Saving");
                        try{await save(audio,document);}catch(Exception error){failed=true;failureMessage=(error as AudioImportException)?.Message;}
                    }
                }
                finally{audio?.Dispose();}
            }
            Update(i,failed?"Failed":token.IsCancellationRequested?"Cancelled":completed?"Completed":"Failed",
                failed?failureMessage??"This file could not finish. Earlier results and original files are preserved.":!completed&&!token.IsCancellationRequested?"Transcription did not finish; check History for received text.":"");
        }
    }
}
