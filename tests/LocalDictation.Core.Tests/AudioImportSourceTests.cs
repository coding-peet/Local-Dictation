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
            await File.WriteAllBytesAsync(path,[3,2,1]);var error=await Assert.ThrowsAsync<AudioImportException>(()=>source.ReadAsync(CancellationToken.None));
            Assert.Contains("changed",error.Message);Assert.DoesNotContain(path,error.Message);
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
    [Theory] [InlineData(0,"empty")] [InlineData(27246593,"too large")]
    public async Task InvalidSourceLengthHasSpecificSafeMessage(long length,string reason)
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".wav");
        try
        {
            using(var file=File.Create(path))file.SetLength(length);
            var error=await Assert.ThrowsAsync<AudioImportException>(()=>AudioImportSource.CaptureAsync(path,CancellationToken.None));
            Assert.Contains(reason,error.Message);Assert.DoesNotContain(path,error.Message);Assert.Equal(length,new FileInfo(path).Length);
        }
        finally{File.Delete(path);}
    }
    [Fact] public async Task MissingSourceHasActionableMessageWithoutItsPath()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".wav");
        var error=await Assert.ThrowsAsync<AudioImportException>(()=>AudioImportSource.CaptureAsync(path,CancellationToken.None));
        Assert.Contains("no longer available",error.Message);Assert.DoesNotContain(path,error.Message);
    }
    [Fact] public async Task LockedSourceHasReadAccessMessageWithoutItsPath()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".wav");
        try
        {
            await File.WriteAllBytesAsync(path,[1,2,3]);
            using var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            var error=await Assert.ThrowsAsync<AudioImportException>(()=>AudioImportSource.CaptureAsync(path,CancellationToken.None));
            Assert.Contains("permissions",error.Message);Assert.Contains("locked",error.Message);Assert.DoesNotContain(path,error.Message);
        }
        finally{File.Delete(path);}
    }
    [Fact] public async Task SourceRemovedAfterCaptureIsReportedWithoutItsPath()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".wav");
        try
        {
            await File.WriteAllBytesAsync(path,[1,2,3]);var source=await AudioImportSource.CaptureAsync(path,CancellationToken.None);File.Delete(path);
            var error=await Assert.ThrowsAsync<AudioImportException>(()=>source.ReadAsync(CancellationToken.None));
            Assert.Contains("no longer available",error.Message);Assert.DoesNotContain(path,error.Message);
        }
        finally{File.Delete(path);}
    }
    [Fact] public async Task SourceLengthChangeIsReportedAsChangedRatherThanEmpty()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".wav");
        try
        {
            await File.WriteAllBytesAsync(path,[1,2,3]);var source=await AudioImportSource.CaptureAsync(path,CancellationToken.None);await File.WriteAllBytesAsync(path,[]);
            var error=await Assert.ThrowsAsync<AudioImportException>(()=>source.ReadAsync(CancellationToken.None));Assert.Contains("changed",error.Message);
        }
        finally{File.Delete(path);}
    }
}
