using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

/// <summary>The ordinary Review stays inside the recording banner.</summary>
public sealed class CompactReviewPanel : Border
{
    readonly AppController controller;
    readonly TextBlock heading=new(),progress=new(),transcript=new(),status=new();
    readonly ScrollViewer transcriptScroll;
    readonly StackPanel alternatives=new(),editing=new();
    readonly Button first,second,edit,keep,destination,details;
    readonly TextBox custom=new();
    readonly Button saveCustom,cancelCustom;
    ReviewDocument? shownDocument;
    List<ReviewPiece>? shownPieces;
    Dictionary<string,ReviewChoice>? shownChoices;
    string? shownSelected,shownManual;
    int shownHistory=-1,shownChoiceCount=-1;
    bool editingPhrase,shownInserted,customEnterPending;
    string? editingId;
    static readonly Brush Ink=Brush("#F7F9FC"),Muted=Brush("#BDC8D9"),Pink=Brush("#FFADB3"),Amber=Brush("#FFD197"),Selected=Brush("#49343F");
    static SolidColorBrush Brush(string value)=>new((Color)ColorConverter.ConvertFromString(value));

    static Button SmallButton(string text,Action action)
    {
        var button=Ui.Button(text,action);
        button.FontSize=11.5;button.MinHeight=27;button.Padding=new(5,3,5,3);button.Margin=new(0,0,7,0);
        button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.Foreground=Muted;
        return button;
    }

    Button WordingButton(string accessibleName,string kind)
    {
        var button=Ui.Button("",()=>controller.ChooseBannerPiece(kind));
        button.MinHeight=38;button.Padding=new(10,7,10,7);button.Margin=new(0,0,0,6);
        button.Background=Brush("#263141");button.BorderBrush=Brush("#536277");button.Foreground=Ink;
        button.HorizontalContentAlignment=HorizontalAlignment.Left;
        button.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            // A second click belongs to the same gesture, even if the first
            // choice has already advanced the panel to another phrase.
            if(e.ClickCount>1)e.Handled=true;
        };
        var enterPending=false;
        button.PreviewKeyDown+=(_,e)=>
        {
            if(e.Key!=Key.Enter)return;
            // ButtonBase clicks on every Enter key-down. Commit on release so
            // held repeats cannot accept the next phrase after auto-advance.
            e.Handled=true;if(!e.IsRepeat)enterPending=true;
        };
        button.PreviewKeyUp+=(_,e)=>
        {
            if(e.Key!=Key.Enter)return;
            e.Handled=true;
            var commit=enterPending;enterPending=false;
            if(commit&&button.IsEnabled&&button.IsKeyboardFocused)controller.ChooseBannerPiece(kind);
        };
        button.LostKeyboardFocus+=(_,_)=>enterPending=false;
        System.Windows.Automation.AutomationProperties.SetName(button,accessibleName);
        return button;
    }

    public CompactReviewPanel(AppController controller)
    {
        this.controller=controller;
        Margin=new(0,12,0,0);Padding=new(13,11,13,11);CornerRadius=new(10);
        Background=Brush("#18202B");MaxHeight=370;
        var column=new StackPanel();
        var title=new DockPanel();heading.FontSize=11;heading.FontWeight=FontWeights.SemiBold;heading.Foreground=Muted;
        progress.FontSize=11;progress.Foreground=Muted;progress.HorizontalAlignment=HorizontalAlignment.Right;
        DockPanel.SetDock(progress,Dock.Right);title.Children.Add(progress);title.Children.Add(heading);column.Children.Add(title);
        transcript.FontSize=16;transcript.LineHeight=25;transcript.TextWrapping=TextWrapping.Wrap;transcript.Foreground=Ink;
        System.Windows.Automation.AutomationProperties.SetName(transcript,"Review transcript; underlined phrases have alternatives");
        transcriptScroll=new ScrollViewer{Content=transcript,MinHeight=40,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,9,0,8)};
        column.Children.Add(transcriptScroll);

        first=WordingButton("First wording","primary");second=WordingButton("Second wording","verification");
        alternatives.Children.Add(first);alternatives.Children.Add(second);column.Children.Add(alternatives);

        custom.AcceptsReturn=false;custom.TextWrapping=TextWrapping.Wrap;custom.MinHeight=38;custom.MaxHeight=65;
        custom.FontSize=14;custom.Foreground=Ink;custom.Background=Brush("#263141");custom.BorderBrush=Brush("#76859A");custom.Padding=new(9,6,9,6);
        System.Windows.Automation.AutomationProperties.SetName(custom,"Edit the selected wording");
        editing.Children.Add(custom);
        saveCustom=SmallButton("Use this wording",SaveCustom);cancelCustom=SmallButton("Cancel",()=>{editingPhrase=false;Refresh();});
        var editActions=Ui.Row(saveCustom,cancelCustom);editActions.Margin=new(0,5,0,5);editing.Children.Add(editActions);column.Children.Add(editing);
        custom.PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Enter){e.Handled=true;if(!e.IsRepeat)customEnterPending=true;}
            else if(e.Key==Key.Escape){e.Handled=true;editingPhrase=false;Refresh();}
        };
        custom.PreviewKeyUp+=(_,e)=>
        {
            if(e.Key!=Key.Enter)return;
            e.Handled=true;
            if(customEnterPending){customEnterPending=false;SaveCustom();}
        };
        custom.LostKeyboardFocus+=(_,_)=>customEnterPending=false;
        custom.TextChanged+=(_,_)=>
        {
            if(!editingPhrase||editingId==null||controller.BannerReview is not {} document||document.InsertedAt!=null)return;
            document.CustomDrafts[editingId]=custom.Text;
            controller.SaveReview(document);
        };

        edit=SmallButton("Edit wording",BeginEdit);
        keep=SmallButton("Keep the remaining wording",controller.KeepBannerWording);
        destination=SmallButton("Choose destination",controller.ChooseBannerDestination);
        details=SmallButton("Details",controller.OpenBannerDetails);
        keep.ToolTip="Keep the displayed wording for every remaining phrase";
        details.ToolTip="Open the full editor and recording details";
        var actions=new WrapPanel{Margin=new(0,2,0,0)};
        foreach(var button in new[]{edit,keep,destination,details})actions.Children.Add(button);
        column.Children.Add(actions);
        status.FontSize=11.5;status.LineHeight=17;status.Foreground=Muted;status.TextWrapping=TextWrapping.Wrap;status.Margin=new(0,7,0,0);
        column.Children.Add(status);
        Child=new ScrollViewer{Content=column,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }

    static string Wording(ReviewDocument document,ReviewPiece piece)=>document.Choices.TryGetValue(piece.Id,out var choice)
        ?choice.Kind switch{"verification"=>piece.Verification,"custom"=>choice.Custom??"",_=>piece.Primary}:piece.Primary;

    static void SetWording(Button button,string value)
    {
        var text=value.Length==0?"Remove this text":value;
        button.Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,
            FontSize=14,LineHeight=20,MaxHeight=40,Foreground=Ink};
        button.ToolTip=text;
        var order=System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("First",StringComparison.Ordinal)?"First wording":"Second wording";
        System.Windows.Automation.AutomationProperties.SetName(button,order+": "+text);
    }

    void BeginEdit()
    {
        var document=controller.BannerReview;
        var piece=document?.Pieces.FirstOrDefault(p=>p.Id==controller.SelectedReviewPieceId&&p.Different);
        if(document==null||piece==null||document.ManualFinalDraft!=null||document.InsertedAt!=null)return;
        editingPhrase=false;
        editingId=piece.Id;custom.Text=document.CustomDrafts.GetValueOrDefault(piece.Id,Wording(document,piece));
        editingPhrase=true;Refresh();custom.Focus();custom.SelectAll();
    }

    void SaveCustom()
    {
        if(!editingPhrase||editingId!=controller.SelectedReviewPieceId||controller.BannerReview?.InsertedAt!=null)return;
        var value=custom.Text;editingPhrase=false;controller.EditBannerPiece(value);
    }

    public void Refresh()
    {
        var document=controller.BannerReview;
        if(document==null){Visibility=Visibility.Collapsed;shownDocument=null;return;}
        Visibility=Visibility.Visible;
        var selected=controller.SelectedReviewPieceId;
        var selectedPiece=document.Pieces.FirstOrDefault(p=>p.Different&&p.Id==selected);
        var inserted=document.InsertedAt!=null;
        if(editingId!=selected||shownDocument!=document||inserted)editingPhrase=false;
        var manual=document.ManualFinalDraft;
        var changed=shownDocument!=document||shownPieces!=document.Pieces||shownChoices!=document.Choices||shownSelected!=selected||
            shownManual!=manual||shownHistory!=document.History.Count||shownChoiceCount!=document.Choices.Count||shownInserted!=inserted;
        if(changed)
        {
            transcript.Inlines.Clear();
            if(manual!=null||inserted)transcript.Inlines.Add(new Run(document.OutputText));
            else
            {
                Hyperlink? active=null;
                foreach(var piece in document.Pieces)
                {
                    var value=Wording(document,piece);
                    if(!piece.Different){transcript.Inlines.Add(new Run(value));continue;}
                    var resolved=document.Choices.ContainsKey(piece.Id);
                    var link=new Hyperlink(new Run(value.Length==0?"∅":value))
                    {
                        Foreground=resolved?Muted:Pink,TextDecorations=TextDecorations.Underline,
                        Background=piece.Id==selected?Selected:Brushes.Transparent,ToolTip="Choose the correct wording",
                        IsEnabled=!controller.IsApplyingReview
                    };
                    link.Click+=(_,_)=>controller.SelectBannerPiece(piece.Id);transcript.Inlines.Add(link);
                    if(piece.Id==selected)active=link;
                }
                if(active!=null)Dispatcher.BeginInvoke(()=>{if(IsVisible)active.BringIntoView();});
            }
            shownDocument=document;shownPieces=document.Pieces;shownChoices=document.Choices;shownSelected=selected;shownManual=manual;
            shownHistory=document.History.Count;shownChoiceCount=document.Choices.Count;
            shownInserted=inserted;
            if(selectedPiece!=null){SetWording(first,selectedPiece.Primary);SetWording(second,selectedPiece.Verification);}
        }
        var applying=controller.IsApplyingReview;
        heading.Text=inserted?"INSERTED TEXT":manual!=null?"EDITED FINAL TEXT":selectedPiece!=null?"CHOOSE THE CORRECT WORDING":"YOUR TEXT";
        progress.Text=inserted?"Already inserted":applying?"Inserting…":document.UnresolvedCount==0?"Ready":$"{document.UnresolvedCount} left";
        alternatives.Visibility=selectedPiece!=null&&manual==null&&!editingPhrase&&!inserted?Visibility.Visible:Visibility.Collapsed;
        editing.Visibility=editingPhrase&&manual==null?Visibility.Visible:Visibility.Collapsed;
        edit.Visibility=selectedPiece!=null&&manual==null&&!editingPhrase&&!inserted?Visibility.Visible:Visibility.Collapsed;
        keep.Visibility=document.UnresolvedCount>0&&manual==null&&!inserted?Visibility.Visible:Visibility.Collapsed;
        destination.Visibility=document.CanInsert?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in new[]{first,second,edit,keep,destination,saveCustom,cancelCustom})button.IsEnabled=!applying&&!inserted;
        details.IsEnabled=!applying;
        custom.IsEnabled=!applying&&!inserted;
        // Updating only enabled state must not rebuild the inline transcript or steal keyboard focus.
        foreach(var link in transcript.Inlines.OfType<Hyperlink>())link.IsEnabled=!applying&&!inserted;
        status.Text=inserted?"This text was already inserted. Details keeps its recording information and original choices available.":manual!=null?"Your edited final draft is kept. Open Details to change it.":document.Reason;
        status.Foreground=applying?Muted:document.UnresolvedCount>0?Muted:Amber;
    }
}
