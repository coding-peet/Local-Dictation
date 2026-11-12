using System.Text;
using System.Text.Json;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

// One bounded active file and one previous file. Diagnostic IO is always best effort.
static class DiagnosticJournal
{
    const int Limit=256*1024;
    static readonly object Sync=new();
    public static bool Enabled=>Environment.GetEnvironmentVariable("LOCAL_DICTATION_DELIVERY_DIAGNOSTICS")=="1";

    public static void Append(string name,object value)
    {
        if(!Enabled)return;
        try
        {
            var line=JsonSerializer.Serialize(value)+Environment.NewLine;
            var size=Encoding.UTF8.GetByteCount(line);
            if(size>Limit)return;
            lock(Sync)
            {
                var directory=Path.Combine(AppPaths.Root,"logs");Directory.CreateDirectory(directory);
                var file=Path.Combine(directory,name);
                if(File.Exists(file)&&new FileInfo(file).Length+size>Limit)File.Move(file,file+".previous",true);
                File.AppendAllText(file,line);
            }
        }
        catch(Exception){}
    }

    public static void Session(object value)
    {
        if(!Enabled)return;
        try
        {
            lock(Sync)
            {
                var directory=Path.Combine(AppPaths.Root,"logs");Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,"delivery-session.json"),JsonSerializer.Serialize(value));
            }
        }
        catch(Exception){}
    }
}
