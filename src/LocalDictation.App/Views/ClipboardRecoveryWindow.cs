using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LocalDictation.App.Views;

/// <summary>Exceptional recovery only; ordinary recording never opens this window.</summary>
public sealed class ClipboardRecoveryWindow : Window
{
    readonly AppController controller;
    readonly TextBlock status=Ui.Text("",12),explanation=Ui.Text("");
    readonly CheckBox inspected=new(){Content="I checked the destination and stopped any delayed paste.",Margin=new(0,12,0,12)};
    readonly Button restore,recheck;
    readonly TextBox details=Ui.Box(false,150);

    public ClipboardRecoveryWindow(AppController controller)
    {
        this.controller=controller;
        Title="Local Dictation - Clipboard recovery";
        Width=560;MinWidth=440;SizeToContent=SizeToContent.Height;MaxHeight=700;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new StackPanel{Margin=new(24)};
        root.Children.Add(Ui.Text("Resolve clipboard recovery",24,true));
        root.Children.Add(explanation);
        root.Children.Add(Ui.Text("Late insertion checks run automatically while the original field is unchanged. They never repeat the paste. If confirmation is unavailable, inspect the destination before restoring.",12));
        recheck=Ui.Button("Retry clipboard restoration",async()=>await controller.RecheckClipboardRecoveryAsync());
        root.Children.Add(recheck);
        root.Children.Add(Ui.Text("Before restoring the clipboard, inspect the destination and stop any delayed paste. A delayed paste could otherwise use your previous clipboard contents.",12));
        root.Children.Add(inspected);
        restore=Ui.Button("Restore previous clipboard",async()=>
        {
            if(inspected.IsChecked!=true)return;
            await controller.RestoreClipboardAfterInspection();
            if(!controller.HasClipboardRecovery&&IsVisible)Close();
        },true);
        root.Children.Add(Ui.Row(restore,Ui.Button("Close",Close)));
        status.SetResourceReference(TextBlock.ForegroundProperty,"Warning");
        status.Margin=new(0,12,0,8);root.Children.Add(status);
        details.FontFamily=new FontFamily("Consolas");details.FontSize=11;
        root.Children.Add(new Expander{Header="Technical details (no transcript or clipboard contents)",Content=details,Margin=new(0,12,0,0)});
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        inspected.Checked+=(_,_)=>Refresh();inspected.Unchecked+=(_,_)=>Refresh();
        controller.Changed+=Refresh;
        Closed+=(_,_)=>controller.Changed-=Refresh;
        Refresh();
    }

    void Refresh()
    {
        restore.IsEnabled=controller.CanRestoreClipboard&&inspected.IsChecked==true&&!controller.IsCheckingClipboard;
        recheck.Visibility=controller.HasAcknowledgedClipboardBackup?Visibility.Visible:Visibility.Collapsed;
        recheck.IsEnabled=controller.HasAcknowledgedClipboardBackup&&!controller.IsCheckingClipboard;
        explanation.Text=controller.HasAcknowledgedClipboardBackup
            ?"The insertion was confirmed, but Windows has not restored the previous clipboard yet. Its backup is still kept."
            :"A paste was sent but its result has not been confirmed. Your transcript is kept in History, and the previous clipboard is held in memory.";
        status.Text=controller.IsCheckingClipboard?"Checking clipboard recovery…":controller.IsBusy?"Finish the current recording with Ctrl+Shift before restoring.":controller.HasClipboardRecovery?controller.ClipboardRecoveryStatus:controller.ConnectionMessage;
        details.Text=controller.ClipboardRecoveryDetails;
        if(IsVisible&&!controller.HasClipboardRecovery)Close();
    }
}
