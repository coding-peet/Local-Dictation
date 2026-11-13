using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

public sealed class HistoryPanel : UserControl,IDisposable
{
    readonly AppController controller;
    readonly ListBox items=new(){DisplayMemberPath="Label",MinHeight=150};
    readonly TextBox transcript=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top};
    readonly TextBlock detail=Ui.Text(""),message=Ui.Text("");
    readonly HistoryAudioPlayerControl playback=new();
    bool refreshing,active,disposed;
    string? loadedAudioId;
    IReadOnlyList<HistoryEntry>? shown;
    public HistoryPanel(AppController controller)
    {
        this.controller=controller;
        var root=new DockPanel{MinHeight=560,Height=560};root.SetResourceReference(Panel.BackgroundProperty,"Canvas");
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
        var body=new Grid{MinHeight=200};body.ColumnDefinitions.Add(new(){Width=new(220)});body.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        body.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var list=Ui.Card(items);list.Padding=new(0);list.Margin=new(0,8,16,0);body.Children.Add(list);
        var selectedText=new DockPanel();detail.Margin=new(0,0,0,16);detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");DockPanel.SetDock(detail,Dock.Top);selectedText.Children.Add(detail);selectedText.Children.Add(transcript);
        var card=Ui.Card(selectedText);card.Margin=new(0,8,0,0);Grid.SetColumn(card,1);body.Children.Add(card);
        root.Children.Add(body);Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch,
            HorizontalContentAlignment=HorizontalAlignment.Stretch};
        bool? stacked=null;
        SizeChanged+=(_,_)=>
        {
            var compact=ActualWidth<600;
            // A finite surface lets the list and transcript scroll internally.
            // Only a viewport smaller than the comfortable layout scrolls the page.
            root.MinHeight=compact?700:560;root.Height=Math.Max(root.MinHeight,ActualHeight);
            if(stacked==compact)return;stacked=compact;
            body.MinHeight=compact?330:200;
            body.ColumnDefinitions[0].Width=compact?new(1,GridUnitType.Star):new(220);
            body.ColumnDefinitions[1].Width=compact?new(0):new(1,GridUnitType.Star);
            body.RowDefinitions.Clear();
            body.RowDefinitions.Add(new(){Height=compact?new(128):new(1,GridUnitType.Star)});
            if(compact)body.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
            items.MinHeight=compact?0:150;list.Margin=compact?new(0,8,0,8):new(0,8,16,0);
            Grid.SetColumn(card,compact?0:1);Grid.SetRow(card,compact?1:0);
        };
        System.Windows.Automation.AutomationProperties.SetName(items,"Recent dictations");
        System.Windows.Automation.AutomationProperties.SetName(transcript,"Selected dictation transcript");
        items.SelectionChanged+=(_,_)=>{if(!refreshing)Select();};
        controller.Changed+=Refresh;
    }
    /// <summary>Only the selected main-window section owns decrypted audio.</summary>
    public void SetActive(bool value)
    {
        if(disposed||active==value)return;active=value;
        if(active){shown=null;Refresh();}
        else{playback.Clear();loadedAudioId=null;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;active=false;controller.Changed-=Refresh;
        playback.Dispose();loadedAudioId=null;
    }
    HistoryEntry? Selected=>items.SelectedItem as HistoryEntry;
    void Refresh()
    {
        if(disposed||!active||ReferenceEquals(shown,controller.History))return;shown=controller.History;
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
        message.Text="";transcript.Text=Selected?.Document.OutputText??"";
        detail.Text=Selected is {} e?$"{e.Document.PrimaryModel} · {e.DurationSeconds:0.0} seconds · {LanguagePolicy.Label(e.Document.RequestedLanguage,e.Document.Language,e.Document.LanguageMode,null)}":"No dictations saved yet.";
        if(!active||disposed)return;
        if(Selected is {} current&&current.Id==loadedAudioId)return;
        playback.Clear();loadedAudioId=null;
        if(Selected is not {} entry)return;
        try{playback.LoadAudio(controller.HistoryAudio(entry.Id));loadedAudioId=entry.Id;}
        catch(Exception){playback.Clear();message.Text="The original recording could not be opened.";}
    }
    void Copy(){if(Selected is not {} entry)return;try{Clipboard.SetText(entry.Document.OutputText);message.Text="Copied.";}catch(Exception){message.Text="Clipboard unavailable. Select and copy the text manually.";}}
    void OpenReview(){if(Selected is {} entry)controller.OpenReview(entry.Document);}
    void Export()
    {
        if(Selected is not {} entry)return;
        var picker=new Microsoft.Win32.SaveFileDialog{Title="Save original recording",Filter="WAV audio|*.wav",FileName=$"dictation-{entry.Document.Created:yyyyMMdd-HHmmss}.wav"};
        var owner=Window.GetWindow(this);
        if((owner==null?picker.ShowDialog():picker.ShowDialog(owner))!=true)return;
        byte[]? bytes=null;
        try{bytes=controller.HistoryAudio(entry.Id);File.WriteAllBytes(picker.FileName,bytes);message.Text="Audio exported to the selected file.";}
        catch(Exception){message.Text="Could not export this recording.";}
        finally{if(bytes!=null)CryptographicOperations.ZeroMemory(bytes);}
    }
    void Delete()
    {
        if(Selected is not {} entry)return;
        if(MessageBox.Show(Window.GetWindow(this),"Delete this transcript and recording from History? Pending Review is kept separately.","Delete dictation",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        playback.Clear();loadedAudioId=null;try{controller.DeleteHistory(entry.Id);message.Text="Deleted.";}catch(Exception){message.Text="Could not delete this history item.";}
    }
}
