using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace LocalDictation.Core;

public sealed record HistoryEntry(ReviewDocument Document,double DurationSeconds)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasAudio {get;init;}=true;
    public string Id=>Document.Id;
    public string Label=>$"{Document.Created.LocalDateTime:g} · {DurationSeconds:0}s · {Document.Primary.ReplaceLineEndings(" ")[..Math.Min(80,Document.Primary.ReplaceLineEndings(" ").Length)]}";
}

public sealed class HistoryStore(string directory)
{
    public List<string> Errors {get;}=[];
    string PathFor(string id,string suffix)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Invalid history ID.");
        return Path.Combine(directory,id+suffix);
    }
    public void Add(ReviewDocument document,byte[] pcm)
    {
        if(pcm.Length==0||pcm.Length%2!=0)throw new ArgumentException("Expected PCM16 audio.");
        var audio=Wave(pcm);
        try {AtomicFile.Write(PathFor(document.Id,".audio"),UserProtection.Transform(audio,false));}
        finally {CryptographicOperations.ZeroMemory(audio);}
        try {Save(new(document,pcm.Length/32000.0));}
        catch {File.Delete(PathFor(document.Id,".audio"));throw;}
    }
    void Save(HistoryEntry entry)
    {
        var plain=JsonSerializer.SerializeToUtf8Bytes(entry);
        try {AtomicFile.Write(PathFor(entry.Id,".history"),UserProtection.Transform(plain,false));}
        finally {CryptographicOperations.ZeroMemory(plain);}
    }
    public void Update(ReviewDocument document)
    {
        var existing=Load().FirstOrDefault(e=>e.Id==document.Id);
        if(existing!=null)Save(existing with {Document=document});
    }
    public IReadOnlyList<HistoryEntry> Load()
    {
        Errors.Clear();if(!Directory.Exists(directory))return [];
        var entries=new List<HistoryEntry>();
        foreach(var path in Directory.EnumerateFiles(directory,"*.history"))
        {
            try
            {
                var plain=UserProtection.Transform(File.ReadAllBytes(path),true);
                try
                {
                    var entry=JsonSerializer.Deserialize<HistoryEntry>(plain)??throw new InvalidDataException();
                    if(entry.Document==null||PathFor(entry.Id,".history")!=path)throw new InvalidDataException();
                    entries.Add(entry with{HasAudio=File.Exists(PathFor(entry.Id,".audio"))});
                }
                finally {CryptographicOperations.ZeroMemory(plain);}
            }
            catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
            {Errors.Add(Path.GetFileName(path));}
        }
        return entries.OrderByDescending(e=>e.Document.Created).ToList();
    }
    /// <summary>Expire only audio belonging to validated history entries. Text is never evicted.</summary>
    public IReadOnlyList<string> ApplyAudioRetention(int retentionDays,DateTimeOffset now)
    {
        if(retentionDays is <0 or >3650)throw new ArgumentOutOfRangeException(nameof(retentionDays),"Choose 0 (Forever) or 1 to 3650 days.");
        if(retentionDays==0)return [];
        var failures=new List<string>();
        var age=TimeSpan.FromDays(retentionDays);
        foreach(var entry in Load())
        {
            if(!entry.HasAudio||now-entry.Document.Created<age)continue;
            try{File.Delete(PathFor(entry.Id,".audio"));}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){failures.Add(entry.Id);}
        }
        return failures;
    }
    public byte[] ReadAudio(string id)=>UserProtection.Transform(File.ReadAllBytes(PathFor(id,".audio")),true);
    public void Delete(string id)
    {
        File.Delete(PathFor(id,".audio"));File.Delete(PathFor(id,".history"));
    }
    static byte[] Wave(byte[] pcm)
    {
        var wave=new byte[44+pcm.Length];
        "RIFF"u8.CopyTo(wave);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4),36+pcm.Length);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8));BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16),16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20),1);BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22),1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24),16000);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28),32000);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32),2);BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34),16);
        "data"u8.CopyTo(wave.AsSpan(36));BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40),pcm.Length);
        pcm.CopyTo(wave,44);return wave;
    }
}
