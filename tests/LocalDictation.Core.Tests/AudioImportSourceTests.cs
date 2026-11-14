using LocalDictation.Core;
using Xunit;
public class AudioImportSourceTests
{
    [Fact] public async Task ChangedSourceIsRefusedAndNeverModified()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".wav");
        try
        {
            await File.WriteAllBytesAsync(path,[1,2,3]);var source=await AudioImportSource.CaptureAsync(path,CancellationToken.None);
            await File.WriteAllBytesAsync(path,[3,2,1]);await Assert.ThrowsAsync<IOException>(()=>source.ReadAsync(CancellationToken.None));
            Assert.Equal(new byte[]{3,2,1},await File.ReadAllBytesAsync(path));
        }
        finally{File.Delete(path);}
    }
    [Fact] public async Task OriginalNameHashAndBytesAreSnapshottedWithoutWriting()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".wav");
        try{await File.WriteAllBytesAsync(path,[1,2,3]);var source=await AudioImportSource.CaptureAsync(path,CancellationToken.None);Assert.Equal(Path.GetFileName(path),source.FileName);Assert.Equal(64,source.Sha256.Length);Assert.Equal(new byte[]{1,2,3},await source.ReadAsync(CancellationToken.None));}
        finally{File.Delete(path);}
    }
}
