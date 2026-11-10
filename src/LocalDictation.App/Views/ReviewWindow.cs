using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

public sealed class ReviewWindow : Window
{
    readonly ReviewDocument document;
    readonly Action<ReviewDocument> save;
    readonly Action<string> dismiss;
    readonly RichTextBox transcript = new() { IsReadOnly=true, VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        MinHeight=210, BorderThickness=new(0), Padding=new(8), FontSize=16 };
    readonly TextBox primary = Ui.Box(false,64), verification = Ui.Box(false,64), custom = Ui.Box(true,62), manual = Ui.Box(true,130);
    readonly TextBlock counter=Ui.Text(""), note=Ui.Text(""), sourceLabel=Ui.Text("");
    readonly Button copy, undo, rebuild, restore;
    readonly StackPanel manualPanel=new();
    readonly Border choiceCard;
    readonly Grid middle=new();
    readonly List<ReviewPiece> differences;
    int selected;
    bool refreshing;
    public string ReviewId => document.Id;

    public ReviewWindow(ReviewDocument document, Action<ReviewDocument> save, Action<string> dismiss)
    {
        this.document=document; this.save=save; this.dismiss=dismiss;
        differences=document.Pieces.Where(p=>p.Different).ToList();
        Title="Review · Local Dictation"; Width=1080; Height=740; MinWidth=650; MinHeight=540;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel { Margin=new(24) };
        var header=new StackPanel();
        header.Children.Add(Ui.Text("Review your dictation",22,true));
        header.Children.Add(Ui.Text(document.Reason,13));
        var provenance=Ui.Text(document.PrimaryModel +
            (document.VerificationModel is null?"":"   →   "+document.VerificationModel),12);
        provenance.Foreground=Ui.Brush("Muted"); header.Children.Add(provenance);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new StackPanel();
        note.Foreground=Ui.Brush("Muted"); note.FontSize=12;
        var buttons=new WrapPanel { Margin=new(0,10,0,8) };
        copy=Ui.Button("_Copy reviewed text",Copy,true);
        undo=Ui.Button("_Undo choice",()=>{document.Undo();Changed();});
        var keep=Ui.Button("_Keep remaining dictation",()=>{document.KeepRemaining();Changed();});
        var close=Ui.Button("Close",Close);
        var remove=Ui.Button("_Dismiss",Dismiss);
        foreach(var button in new[]{copy,undo,keep,close,remove}) { button.Margin=new(0,0,8,8); buttons.Children.Add(button); }
        footer.Children.Add(buttons);
        footer.Children.Add(note);
        footer.Children.Add(Ui.Text("Copy and Close keep this item. Dismiss permanently removes this pending Review.",12));
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);

        var working=new StackPanel();
        working.Children.Add(Ui.Text("Working transcript",16,true));
        working.Children.Add(Ui.Text("Select a highlighted phrase, then choose its wording. Both models can be wrong.",12));
        working.Children.Add(transcript);
        working.Children.Add(Ui.Button("_Edit final text",()=>{document.ManualFinalDraft=document.OutputText;Changed();}));
        manualPanel.Children.Add(Ui.Text("Final manual draft",14,true));
        manualPanel.Children.Add(Ui.Text("This draft takes precedence. Changing a phrase will keep your manual wording.",12));
        manualPanel.Children.Add(manual);
        rebuild=Ui.Button("Rebuild from choices",()=>
        {
            if(MessageBox.Show(this,"Rebuild the final draft from your phrase choices? Your previous manual draft will remain available.",
                "Rebuild final text",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)
            {document.RebuildDraft();Changed();}
        });
        restore=Ui.Button("Restore saved manual draft",ShowDraftArchive);
        manualPanel.Children.Add(Ui.Row(rebuild,restore)); working.Children.Add(manualPanel);
        var left=Ui.Card(working); left.Margin=new(0,0,16,12);

        var choices=new StackPanel();
        choices.Children.Add(Ui.Text("Compare this phrase",16,true));
        choices.Children.Add(Ui.Row(
            Ui.Button("← Previous",()=>Select(selected-1)),Ui.Button("Next →",()=>Select(selected+1))));
        choices.Children.Add(counter); choices.Children.Add(sourceLabel);
        Ui.Label(choices,"Dictation",primary);
        choices.Children.Add(Ui.Button("Use _dictation",()=>Choose("primary")));
        Ui.Label(choices,"Verification",verification);
        choices.Children.Add(Ui.Button("Use _verification",()=>Choose("verification")));
        Ui.Label(choices,"Your wording",custom,"An empty value deliberately removes this phrase.");
        choices.Children.Add(Ui.Button("Use _my wording",()=>Choose("custom")));
        choiceCard=Ui.Card(choices);
        middle.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        middle.ColumnDefinitions.Add(new(){Width=new(340)});
        middle.RowDefinitions.Add(new(){Height=GridLength.Auto});
        middle.RowDefinitions.Add(new(){Height=GridLength.Auto});
        middle.Children.Add(left);middle.Children.Add(choiceCard);Grid.SetColumn(choiceCard,1);
        var scroller=new ScrollViewer { Content=middle,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        root.Children.Add(scroller); Content=root;
        SizeChanged+=(_,_) =>
        {
            var narrow=ActualWidth<920;
            middle.ColumnDefinitions[1].Width=narrow?new(0):new(340);
            Grid.SetColumn(choiceCard,narrow?0:1);Grid.SetRow(choiceCard,narrow?1:0);
            left.Margin=narrow?new(0,0,0,12):new(0,0,16,12);
        };
        custom.TextChanged+=(_,_) =>
        {
            if(refreshing||differences.Count==0)return;
            document.CustomDrafts[differences[selected].Id]=custom.Text; save(document);
        };
        manual.TextChanged+=(_,_) =>
        {
            if(refreshing)return;
            document.ManualFinalDraft=manual.Text;save(document);
        };
        Refresh();
    }

    void Select(int index)
    {
        if(differences.Count==0)return;
        selected=(index+differences.Count)%differences.Count;
        Refresh();
    }
    void Choose(string kind)
    {
        if(differences.Count==0)return;
        document.Choose(differences[selected].Id,kind,custom.Text);Changed();
    }
    void Changed() { save(document);Refresh(); }
    void Refresh()
    {
        refreshing=true;
        try
        {
            var flow=new FlowDocument { PagePadding=new(4), FontFamily=new("Segoe UI"), FontSize=16 };
            var paragraph=new Paragraph { LineHeight=27 };
            foreach(var piece in document.Pieces)
            {
                var text=document.Choices.TryGetValue(piece.Id,out var choice)
                    ? choice.Kind switch {"verification"=>piece.Verification,"custom"=>choice.Custom??"",_=>piece.Primary}
                    :piece.Primary;
                if(!piece.Different) {paragraph.Inlines.Add(new Run(text));continue;}
                var index=differences.FindIndex(p=>p.Id==piece.Id);
                var link=new Hyperlink(new Run(text.Length==0?"∅":text))
                {
                    Foreground=Ui.Brush("Ink"), Background=index==selected?Ui.Brush("Selected"):new SolidColorBrush(Color.FromRgb(255,243,207)),
                    TextDecorations=TextDecorations.Underline, ToolTip="Compare phrase "+(index+1)
                };
                link.Click+=(_,_)=>Select(index);paragraph.Inlines.Add(link);
            }
            flow.Blocks.Add(paragraph);transcript.Document=flow;
            choiceCard.Visibility=differences.Count==0?Visibility.Collapsed:Visibility.Visible;
            if(differences.Count>0)
            {
                var piece=differences[selected];
                counter.Text=$"Difference {selected+1} of {differences.Count} · {document.UnresolvedCount} unresolved";
                sourceLabel.Text=document.Choices.TryGetValue(piece.Id,out var selectedChoice)
                    ?"Selected: "+selectedChoice.Kind:"Not reviewed · dictation is kept for now";
                primary.Text=piece.Primary;verification.Text=piece.Verification;
                primary.ToolTip=piece.Primary.Length==0?"Empty alternative (no text)":null;
                verification.ToolTip=piece.Verification.Length==0?"Empty alternative (no text)":null;
                custom.Text=document.CustomDrafts.GetValueOrDefault(piece.Id,
                    document.Choices.GetValueOrDefault(piece.Id)?.Custom??piece.Primary);
            }
            copy.IsEnabled=document.UnresolvedCount==0;
            undo.IsEnabled=document.History.Count>0;
            manualPanel.Visibility=document.ManualFinalDraft!=null||document.PreviousManualDraft!=null?Visibility.Visible:Visibility.Collapsed;
            manual.Visibility=document.ManualFinalDraft!=null?Visibility.Visible:Visibility.Collapsed;
            manual.Text=document.ManualFinalDraft??"";
            rebuild.IsEnabled=document.ManualFinalDraft!=null;
            restore.IsEnabled=document.PreviousManualDraft!=null||document.ManualDraftArchive.Count>0;
            note.Text=document.UnresolvedCount>0
                ?"Resolve each difference, or use Keep remaining dictation to enable Copy. ∅ marks an empty alternative."
                :"Ready to copy plain text. Check any text already inserted before pasting.";
        }
        finally {refreshing=false;}
    }
    void ShowDraftArchive()
    {
        var menu=new ContextMenu();
        var versions=document.ManualDraftArchive.ToList();
        if(document.PreviousManualDraft!=null&&!versions.Contains(document.PreviousManualDraft))versions.Add(document.PreviousManualDraft);
        foreach(var version in versions.AsEnumerable().Reverse())
        {
            var item=new MenuItem{Header=version.Length==0?"Empty draft":version[..Math.Min(version.Length,70)],ToolTip=version};
            item.Click+=(_,_)=>
            {
                if(document.ManualFinalDraft!=null&&!document.ManualDraftArchive.Contains(document.ManualFinalDraft))
                    document.ManualDraftArchive.Add(document.ManualFinalDraft);
                document.ManualFinalDraft=version;Changed();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget=restore;menu.IsOpen=true;
    }
    void Copy()
    {
        if(document.UnresolvedCount!=0)return;
        System.Windows.Clipboard.SetText(document.OutputText);
        note.Text="Copied. This item remains in Review until you dismiss it.";
    }
    void Dismiss()
    {
        if(MessageBox.Show(this,"Remove this pending Review? There is no saved audio recording.",
            "Dismiss Review",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        dismiss(document.Id);Close();
    }
}
