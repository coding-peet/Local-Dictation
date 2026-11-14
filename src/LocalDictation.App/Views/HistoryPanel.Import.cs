using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using LocalDictation.Core;
namespace LocalDictation.App.Views;

public sealed partial class HistoryPanel
{
    Button importAudio=null!,startImport=null!,cancelImport=null!;
    readonly TextBlock importDescription=Ui.Text("",12),importSummary=Ui.Text("",12);
    readonly StackPanel importRows=new();
    string[] selectedImportPaths=[];
    FrameworkElement ImportToolbar()
    {
        var panel=new StackPanel();
        importAudio=Ui.Button("Import audio…",SelectImport);startImport=Ui.Button("Start import",StartImport,true);cancelImport=Ui.Button("Cancel import",controller.CancelAudioImport);
        panel.Children.Add(Ui.Row(startImport,cancelImport));panel.Children.Add(importDescription);panel.Children.Add(importSummary);
        panel.Children.Add(new ScrollViewer{Content=importRows,MaxHeight=112,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        return panel;
    }
    void SelectImport()
    {
        if(controller.IsBusy){message.Text="Finish the current operation before importing audio.";return;}
        if(controller.PrivateNextRecording){message.Text="Imports are saved to History. Turn off Private for next microphone recording before importing.";return;}
        if(!controller.SetShortcutCapture(true)){message.Text="Finish the current operation before importing audio.";return;}
        try
        {
            var picker=new Microsoft.Win32.OpenFileDialog{Title="Import audio",Multiselect=true,CheckFileExists=true,
                Filter="Audio files|*.wav;*.mp3;*.flac;*.m4a;*.ogg;*.opus|All files|*.*"};
            var owner=Window.GetWindow(this);if((owner==null?picker.ShowDialog():picker.ShowDialog(owner))!=true)return;
            selectedImportPaths=picker.FileNames;RefreshImport();
        }
        finally{controller.SetShortcutCapture(false);}
    }
    void StartImport()
    {
        var error=controller.StartAudioImport(selectedImportPaths);
        if(error!=null)message.Text=error;else selectedImportPaths=[];
        RefreshImport();
    }
    void RefreshImport()
    {
        if(importAudio==null)return;
        importAudio.IsEnabled=!controller.IsBusy&&!controller.IsApplyingReview&&!controller.PrivateNextRecording;
        startImport.Visibility=selectedImportPaths.Length>0?Visibility.Visible:Visibility.Collapsed;startImport.IsEnabled=importAudio.IsEnabled;
        cancelImport.Visibility=controller.IsImporting?Visibility.Visible:Visibility.Collapsed;cancelImport.IsEnabled=controller.IsImporting;
        importDescription.Text=selectedImportPaths.Length==0?"":$"{selectedImportPaths.Length} files · {controller.AudioImportSettingsSummary}";
        importDescription.Visibility=selectedImportPaths.Length==0?Visibility.Collapsed:Visibility.Visible;
        importSummary.Text=controller.ImportSummary;importSummary.Visibility=string.IsNullOrEmpty(importSummary.Text)?Visibility.Collapsed:Visibility.Visible;
        importRows.Children.Clear();
        foreach(var row in controller.ImportItems)
        {
            var line=Ui.Text($"{row.FileName} · {row.State}"+(row.Message.Length>0?$" · {row.Message}":""),12);line.Margin=new(0,2,0,2);importRows.Children.Add(line);
        }
    }
}
