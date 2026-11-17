using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LocalDictation.App.Services;

public sealed partial class WindowsTextDelivery
{
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint process);
    public static void ActivateHistoryDestination(HistoryInsertionDestination destination,CancellationToken token)
    {
        void Validate()
        {
            token.ThrowIfCancellationRequested();
            GetWindowThreadProcessId(destination.Window,out var process);
            if(!IsWindow(destination.Window)||process!=destination.ProcessId||process==Environment.ProcessId)
                throw new InvalidOperationException("The previous destination is no longer available. Click its text field and try Insert again.");
            var foreground=GetForegroundWindow();GetWindowThreadProcessId(foreground,out var foregroundProcess);
            if(foreground!=0&&foreground!=destination.Window&&foregroundProcess!=Environment.ProcessId)
                throw new InvalidOperationException("The foreground application changed. Your saved transcript is unchanged.");
        }
        Validate();
        if(Integrity(Environment.ProcessId)<Integrity(destination.ProcessId))
            throw new InvalidOperationException("The destination runs with higher privileges. Use a destination with the same privileges as Local Dictation.");
        if(!WaitForKeyRelease(DeliveryKeysDown,Validate,()=>Thread.Sleep(10)))
            throw new InvalidOperationException("Release Enter, Space and modifier keys, then try Insert again.");
        Validate();
        if(IsIconic(destination.Window))ShowWindow(destination.Window,9);
        Validate();
        if(!SetForegroundWindow(destination.Window)&&GetForegroundWindow()!=destination.Window)
            throw new InvalidOperationException("Windows could not return to the destination. Click its field and try Insert again.");
        var watch=System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Validate();var foreground=GetForegroundWindow();
            if(foreground==destination.Window)
            {
                try
                {
                    var focused=AutomationElement.FocusedElement;
                    if(focused!=null&&focused.Current.ProcessId!=Environment.ProcessId&&
                        AutomationWindowScope.Resolve(focused,destination.Window,token).Status==AutomationWindowScope.ScopeStatus.Matched)return;
                }
                catch(Exception error) when(error is InvalidOperationException or ElementNotAvailableException or COMException){}
            }
            else if(foreground!=0)
                throw new InvalidOperationException("The destination changed during insertion. Your saved transcript is unchanged.");
            Thread.Sleep(10);
        }while(watch.ElapsedMilliseconds<500);
        throw new InvalidOperationException("The destination field did not become ready. Click its text field and try Insert again.");
    }
}
