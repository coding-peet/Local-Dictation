using System.Security.Cryptography;
namespace LocalDictation.Core;

/// <summary>Immutable identity of a user-selected source; reopening never writes or accepts changed bytes.</summary>
public sealed record AudioImportSource(string FilePath,string FileName,long Length,string Sha256)
{
    public const int MaxEncodedBytes=26*1024*1024-16384;
    static FileStream Open(string path)=>new(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
    static void CheckLength(long length){if(length<=0||length>MaxEncodedBytes)throw new InvalidDataException("Choose a nonempty audio file below 26 MiB.");}
    public static async Task<AudioImportSource> CaptureAsync(string path,CancellationToken token)
    {
        await using var stream=Open(path);CheckLength(stream.Length);
        var hash=await SHA256.HashDataAsync(stream,token);
        return new(path,Path.GetFileName(path),stream.Length,Convert.ToHexString(hash).ToLowerInvariant());
    }
    public async Task<byte[]> ReadAsync(CancellationToken token)
    {
        await using var stream=Open(FilePath);CheckLength(stream.Length);
        if(stream.Length!=Length)throw new IOException("The selected file changed before import.");
        var data=new byte[(int)Length];
        try
        {
            await stream.ReadExactlyAsync(data,token);
            if(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()!=Sha256)throw new IOException("The selected file changed before import.");
            return data;
        }
        catch{CryptographicOperations.ZeroMemory(data);throw;}
    }
}
