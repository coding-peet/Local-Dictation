using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

public sealed class HistoryWindow : Window
{
    readonly AppController controller;
    readonly ListBox items=new(){DisplayMemberPath="Label",MinHeight=150};
    readonly TextBox transcript=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top};
    readonly TextBlock detail=Ui.Text(""),message=Ui.Text("");
    readonly HistoryAudioPlayerControl playback=new();
    bool refreshing;
    IReadOnlyList<HistoryEntry>? shown;
    public HistoryWindow(AppController controller)
    {
        this.controller=controller;Title="Local Dictation · History";Width=960;Height=740;MinWidth=640;MinHeight=540;
        var root=new DockPanel{Margin=new(24)};
        var header=new StackPanel();header.Children.Add(Ui.Text("Recent dictations",24));
        header.Children.Add(Ui.Text("The latest 20 dictations and recordings, protected for your Windows account.",14));
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new StackPanel{Margin=new(0,16,0,0)};
        playback.Margin=new(0,0,0,8);playback.PlaybackError+=text=>message.Text=text;footer.Children.Add(playback);
        var actions=new WrapPanel();
        foreach(var button in new[]{Ui.Button("Copy text",Copy),Ui.Button("Open Review",OpenReview),
            Ui.Button("Change language…",()=>{if(Selected is {} entry)controller.ShowInputLanguage(entry.Document);}),
            Ui.Button("Save audio…",Export),Ui.Button("Delete",Delete)}){button.Margin=new(0,0,8,8);actions.Children.Add(button);}
        footer.Children.Add(actions);footer.Children.Add(message);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new(280)});body.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var list=Ui.Card(items);list.Padding=new(0);list.Margin=new(0,8,16,0);body.Children.Add(list);
        var selectedText=new DockPanel();detail.Margin=new(0,0,0,16);detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");DockPanel.SetDock(detail,Dock.Top);selectedText.Children.Add(detail);selectedText.Children.Add(transcript);
        var card=Ui.Card(selectedText);card.Margin=new(0,8,0,0);Grid.SetColumn(card,1);body.Children.Add(card);
        root.Children.Add(body);Content=root;
        SizeChanged+=(_,_)=>{var compact=ActualWidth<800;body.ColumnDefinitions[0].Width=new(compact?200:280);};
        System.Windows.Automation.AutomationProperties.SetName(items,"Recent dictations");
        System.Windows.Automation.AutomationProperties.SetName(transcript,"Selected dictation transcript");
        items.SelectionChanged+=(_,_)=>{if(!refreshing)Select();};Closed+=(_,_)=>{playback.Dispose();controller.Changed-=Refresh;};
        controller.Changed+=Refresh;Refresh();
    }
    HistoryEntry? Selected=>items.SelectedItem as HistoryEntry;
    void Refresh()
    {
        if(ReferenceEquals(shown,controller.History))return;shown=controller.History;
        var id=Selected?.Id;refreshing=true;
        try
        {
            items.ItemsSource=controller.History;
            items.SelectedItem=controller.History.FirstOrDefault(e=>e.Id==id)??controller.History.FirstOrDefault();
        }
        finally{refreshing=false;}
        Select();
    }
    void Select()
    {
        playback.Clear();message.Text="";transcript.Text=Selected?.Document.OutputText??"";
        detail.Text=Selected is {} e?$"{e.Document.PrimaryModel} · {e.DurationSeconds:0.0} seconds · {LanguagePolicy.Label(e.Document.RequestedLanguage,e.Document.Language,e.Document.LanguageMode,null)}":"No dictations saved yet.";
        if(Selected is not {} entry)return;
        try{playback.LoadAudio(controller.HistoryAudio(entry.Id));}
        catch(Exception){playback.Clear();message.Text="The original recording could not be opened.";}
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
        playback.Clear();try{controller.DeleteHistory(entry.Id);message.Text="Deleted.";}catch(Exception){message.Text="Could not delete this history item.";}
    }
}
