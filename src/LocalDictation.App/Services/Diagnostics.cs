using System.Text.Json;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

public static class Diagnostics
{
    public static void Error(string stage,Exception error)
    {
        try
        {
            var directory=Path.Combine(AppPaths.Root,"logs");Directory.CreateDirectory(directory);
            var file=Path.Combine(directory,"client-errors.jsonl");
            if(File.Exists(file)&&new FileInfo(file).Length>1024*1024)File.Move(file,file+".previous",true);
            File.AppendAllText(file,JsonSerializer.Serialize(new{Time=DateTimeOffset.UtcNow,Stage=stage,
                Type=error.GetType().FullName,Code=$"0x{error.HResult:X8}",Stack=error.StackTrace})+Environment.NewLine);
        }
        catch(Exception){}
    }
}
