using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using LocalDictation.Core;
using NAudio.Wave;

namespace LocalDictation.App.Views;

public sealed class HistoryWindow : Window
{
    readonly AppController controller;
    readonly ListBox items=new(){DisplayMemberPath="Label",MinHeight=150,MaxHeight=270};
    readonly TextBox transcript=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly TextBlock detail=Ui.Text(""),message=Ui.Text("");
    WaveOut? player;WaveFileReader? reader;byte[]? audio;
    IReadOnlyList<HistoryEntry>? shown;
    public HistoryWindow(AppController controller)
    {
        this.controller=controller;Title="Local Dictation · History";Width=800;Height=700;MinWidth=560;MinHeight=500;
        var root=new DockPanel{Margin=new(24)};
        var header=new StackPanel();header.Children.Add(Ui.Text("Recent dictations",24,true));
        header.Children.Add(Ui.Text("The latest 20 dictations and recordings, protected for your Windows account. Older items are removed automatically.",13));
        header.Children.Add(items);header.Children.Add(detail);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new StackPanel{Margin=new(0,12,0,0)};
        var actions=new WrapPanel();
        foreach(var button in new[]{Ui.Button("Play audio",Play),Ui.Button("Stop",Stop),
            Ui.Button("Copy text",Copy),Ui.Button("Open Review",OpenReview),
            Ui.Button("Save audio…",Export),Ui.Button("Delete",Delete)}){button.Margin=new(0,0,8,8);actions.Children.Add(button);}
        footer.Children.Add(actions);footer.Children.Add(message);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        root.Children.Add(transcript);Content=root;
        items.SelectionChanged+=(_,_)=>Select();Closed+=(_,_)=>{Stop();controller.Changed-=Refresh;};
        controller.Changed+=Refresh;Refresh();
    }
    HistoryEntry? Selected=>items.SelectedItem as HistoryEntry;
    void Refresh()
    {
        if(ReferenceEquals(shown,controller.History))return;shown=controller.History;
        var id=Selected?.Id;items.ItemsSource=controller.History;
        items.SelectedItem=controller.History.FirstOrDefault(e=>e.Id==id)??controller.History.FirstOrDefault();
        Select();
    }
    void Select()
    {
        Stop();transcript.Text=Selected?.Document.OutputText??"";
        detail.Text=Selected is {} e?$"{e.Document.PrimaryModel} · {e.DurationSeconds:0.0} seconds":"No dictations saved yet.";
    }
    void Play()
    {
        if(Selected is not {} entry)return;Stop();
        try
        {
            audio=controller.HistoryAudio(entry.Id);reader=new WaveFileReader(new MemoryStream(audio,false));
            player=new WaveOut();player.Init(reader);
            var playing=player;
            player.PlaybackStopped+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(ReferenceEquals(player,playing)){message.Text="Playback stopped.";Stop();}});
            player.Play();message.Text="Playing original recording…";
        }
        catch(Exception){Stop();message.Text="Audio could not be played. Check your output device and saved recording.";}
    }
    void Stop()
    {
        var old=player;player=null;
        if(old!=null){old.Stop();old.Dispose();}
        reader?.Dispose();reader=null;
        if(audio!=null)CryptographicOperations.ZeroMemory(audio);audio=null;
    }
    void Copy(){if(Selected is not {} entry)return;try{Clipboard.SetText(entry.Document.OutputText);message.Text="Copied.";}catch(Exception){message.Text="Clipboard unavailable. Select and copy the text manually.";}}
    void OpenReview(){if(Selected is {} entry)controller.OpenReview(entry.Document);}
    void Export()
    {
        if(Selected is not {} entry)return;
        var picker=new Microsoft.Win32.SaveFileDialog{Title="Save original recording",Filter="WAV audio|*.wav",FileName=$"dictation-{entry.Document.Created:yyyyMMdd-HHmmss}.wav"};
        if(picker.ShowDialog(this)!=true)return;
        byte[]? bytes=null;
        try{bytes=controller.HistoryAudio(entry.Id);File.WriteAllBytes(picker.FileName,bytes);message.Text="Audio exported to the selected file.";}
        catch(Exception){message.Text="Could not export this recording.";}
        finally{if(bytes!=null)CryptographicOperations.ZeroMemory(bytes);}
    }
    void Delete()
    {
        if(Selected is not {} entry)return;
        if(MessageBox.Show(this,"Delete this transcript and recording from History? Pending Review is kept separately.","Delete dictation",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        Stop();try{controller.DeleteHistory(entry.Id);message.Text="Deleted.";}catch(Exception){message.Text="Could not delete this history item.";}
    }
}
