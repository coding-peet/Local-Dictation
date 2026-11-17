namespace LocalDictation.App.Services;

/// <summary>Retains the external destination across Local Dictation and shell navigation.</summary>
public sealed class HistoryDestinationMemory(int ownProcess)
{
    HistoryInsertionDestination? current;
    public HistoryInsertionDestination? Current=>Volatile.Read(ref current);
    public void Observe(nint window,int process,string windowClass)
    {
        if(window==0||process==0||process==ownProcess)return;
        if(new[]{"Shell_TrayWnd","Shell_SecondaryTrayWnd","Progman","WorkerW"}.Contains(windowClass,StringComparer.OrdinalIgnoreCase))return;
        Volatile.Write(ref current,new(window,process));
    }
    public void Clear()=>Volatile.Write(ref current,null);
}
