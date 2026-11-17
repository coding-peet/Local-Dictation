using System.Security.Cryptography;
namespace LocalDictation.Core;

/// <summary>Immutable identity of a user-selected source; reopening never writes or accepts changed bytes.</summary>
public sealed record AudioImportSource(string FilePath,string FileName,long Length,string Sha256)
{
    public const int MaxEncodedBytes=26*1024*1024-16384;
    static FileStream Open(string path)=>new(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
    public static async Task<AudioImportSource> CaptureAsync(string path,CancellationToken token)
    {
        try
        {
            await using var stream=Open(path);AudioImportErrors.CheckEncodedLength(stream.Length);
            var hash=await SHA256.HashDataAsync(stream,token);
            return new(path,Path.GetFileName(path),stream.Length,Convert.ToHexString(hash).ToLowerInvariant());
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {throw new AudioImportException(AudioImportErrors.Message(error,AudioImportPhase.Preparation));}
    }
    public async Task<byte[]> ReadAsync(CancellationToken token)
    {
        try
        {
            await using var stream=Open(FilePath);
            if(stream.Length!=Length)throw new AudioImportException(AudioImportErrors.ChangedFile);
            AudioImportErrors.CheckEncodedLength(stream.Length);
            var data=new byte[(int)Length];
            try
            {
                await stream.ReadExactlyAsync(data,token);
                if(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()!=Sha256)throw new AudioImportException(AudioImportErrors.ChangedFile);
                return data;
            }
            catch{CryptographicOperations.ZeroMemory(data);throw;}
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {throw new AudioImportException(AudioImportErrors.Message(error,AudioImportPhase.Preparation));}
    }
}
