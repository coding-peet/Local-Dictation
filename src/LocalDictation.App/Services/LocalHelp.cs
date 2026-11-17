using System.ComponentModel;
using System.Diagnostics;

namespace LocalDictation.App.Services;

public static class LocalHelp
{
    public static string DocumentPath=>Path.Combine(AppContext.BaseDirectory,"Help.html");

    public static bool TryOpen(out string error)
    {
        var path=DocumentPath;
        if(!File.Exists(path))
        {error="The local Help file is missing: "+path;return false;}
        try
        {
            Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
            error="";return true;
        }
        catch(Exception ex) when(ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {error="Help could not open in your default browser. Check the Windows app association for HTML files. The file is at "+path;return false;}
    }
}
