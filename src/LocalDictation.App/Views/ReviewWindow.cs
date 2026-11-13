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
    readonly Func<ReviewDocument,Task<ReviewInsertionResult>> accept;
    readonly Action<ReviewDocument> chooseDestination;
    readonly RichTextBox transcript = new() { IsReadOnly=true, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalContentAlignment=VerticalAlignment.Top,
        MinHeight=210, BorderThickness=new(0), Padding=new(16), FontSize=16 };
    readonly TextBox primary = Ui.Box(false,64), verification = Ui.Box(false,64), custom = Ui.Box(true,62), manual = Ui.Box(true,130);
    readonly TextBlock counter=Ui.Text(""), note=Ui.Text(""), sourceLabel=Ui.Text("");
    readonly Button copy,acceptButton,destinationButton,undo,rebuild,restore;
    readonly StackPanel manualPanel=new();
    readonly Border choiceCard;
    readonly Grid middle=new();
    readonly List<ReviewPiece> differences;
    int selected;
    bool refreshing;
    public string ReviewId => document.Id;
    public bool IsApplying {get;private set;}

    public ReviewWindow(ReviewDocument document,Action<ReviewDocument> save,Action<string> dismiss,
        Func<ReviewDocument,Task<ReviewInsertionResult>> accept,Action<ReviewDocument> chooseDestination)
    {
        this.document=document; this.save=save; this.dismiss=dismiss;
        this.accept=accept;this.chooseDestination=chooseDestination;
        differences=document.Pieces.Where(p=>p.Different).ToList();
        Title="Review · Local Dictation"; Width=1080; Height=740; MinWidth=650; MinHeight=540;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel { Margin=new(24) };
        System.Windows.Automation.AutomationProperties.SetName(transcript,"Working transcript with phrase choices");
        var header=new StackPanel();
        header.Children.Add(Ui.Text("Review your dictation",22,true));
        header.Children.Add(Ui.Text(document.Reason,13));
        var pinned=document.Provenance;
        static string Revision(string? value)=>string.IsNullOrEmpty(value)?"revision unknown":value[..Math.Min(value.Length,12)];
        var provenance=Ui.Text(document.PrimaryModel+" · "+Revision(pinned?.Primary.Revision)+
            (document.VerificationModel is null?"":"   →   "+document.VerificationModel+" · "+Revision(pinned?.Verification?.Revision)),12);
        provenance.SetResourceReference(TextBlock.ForegroundProperty,"Muted");header.Children.Add(provenance);
        var details=Ui.Box(false,72);
        details.Text=pinned is null?"This older Review has no saved session or revision snapshot.":
            $"Session: {pinned.SessionId}\nSession started: {pinned.SessionStartedUtc?.ToString("O")??"unknown"}\n"+
            $"Dictation revision: {pinned.Primary.Revision??"unknown"}\nVerification revision: {pinned.Verification?.Revision??"unknown"}\nComparison: {pinned.ComparisonId??"not available"}";
        header.Children.Add(new Expander{Header="Recording details",Content=details,Margin=new(0,0,0,12)});
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new StackPanel();
        note.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); note.FontSize=12;
        var buttons=new WrapPanel { Margin=new(0,10,0,8) };
        acceptButton=Ui.Button("_Accept and insert",()=>_ = Accept(),true);
        destinationButton=Ui.Button("Choose destination",()=>this.chooseDestination(document));
        copy=Ui.Button("_Copy reviewed text",Copy);
        undo=Ui.Button("_Undo choice",()=>{document.Undo();Changed();});
        var keep=Ui.Button("_Keep remaining dictation",()=>{document.KeepRemaining();Changed();});
        var close=Ui.Button("Close",Close);
        var remove=Ui.Button("_Dismiss",Dismiss);
        foreach(var button in new[]{acceptButton,destinationButton,copy,undo,keep,close,remove}) { button.Margin=new(0,0,8,8); buttons.Children.Add(button); }
        footer.Children.Add(buttons);
        footer.Children.Add(note);
        footer.Children.Add(Ui.Text("Accept inserts the reviewed text and closes this window. Close keeps the pending Review for later.",12));
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
        var tabs=new TabControl();
        tabs.Items.Add(new TabItem{Header="_Reviewed text",Content=scroller});
        tabs.Items.Add(new TabItem{Header="_Dictation original",Content=Original(document.Primary,document.PrimaryModel,true,ReviewCopySource.Dictation)});
        if(document.VerificationModel!=null)
            tabs.Items.Add(new TabItem{Header="_Verification original",Content=Original(document.Verification,
                document.VerificationModel,document.HasCompletedVerification,ReviewCopySource.Verification)});
        root.Children.Add(tabs); Content=root;
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
        Closing+=(_,e)=>{if(IsApplying){e.Cancel=true;note.Text="Finishing the insertion check…";}};
        Refresh();
    }

    UIElement Original(string source,string model,bool confirmed,ReviewCopySource kind)
    {
        var page=new StackPanel{Margin=new(16)};
        page.Children.Add(Ui.Text("Original model output",17,true));
        page.Children.Add(Ui.Text(model,13));
        page.Children.Add(Ui.Text(confirmed?"This source stays unchanged when you choose phrases or edit the final draft.":
            "This item has no confirmed completed verification. Your dictation is preserved.",13));
        var text=Ui.Box(false,240);text.Text=confirmed?source:"";
        text.SetResourceReference(Control.BackgroundProperty,"Canvas");text.SetResourceReference(Control.BorderBrushProperty,"Border");
        System.Windows.Automation.AutomationProperties.SetName(text,"Original transcript from "+model);
        page.Children.Add(text);
        var copySource=Ui.Button("Copy this original",()=>
        {
            note.Text=ReviewCopy.Copy(document,kind,System.Windows.Clipboard.SetText).Message;
        });
        copySource.IsEnabled=confirmed&&source.Length>0;page.Children.Add(copySource);
        if(confirmed&&source.Length==0)page.Children.Add(Ui.Text("This model returned no text.",13));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
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
            var flow=new FlowDocument { PagePadding=new(4), FontFamily=(FontFamily)System.Windows.Application.Current.FindResource("MaterialDesignFont"), FontSize=16 };
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
                    TextDecorations=TextDecorations.Underline, ToolTip="Compare phrase "+(index+1)
                };
                link.SetResourceReference(TextElement.ForegroundProperty,index==selected?"SelectedInk":"DifferenceInk");
                link.SetResourceReference(TextElement.BackgroundProperty,index==selected?"Selected":"Difference");
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
            acceptButton.IsEnabled=document.CanInsert&&!IsApplying;
            destinationButton.IsEnabled=document.CanInsert&&!IsApplying;
            undo.IsEnabled=document.History.Count>0;
            manualPanel.Visibility=document.ManualFinalDraft!=null||document.PreviousManualDraft!=null?Visibility.Visible:Visibility.Collapsed;
            manual.Visibility=document.ManualFinalDraft!=null?Visibility.Visible:Visibility.Collapsed;
            manual.Text=document.ManualFinalDraft??"";
            rebuild.IsEnabled=document.ManualFinalDraft!=null;
            restore.IsEnabled=document.PreviousManualDraft!=null||document.ManualDraftArchive.Count>0;
            note.Text=document.UnresolvedCount>0
                ?"Resolve each difference, or use Keep remaining dictation to enable Accept. ∅ marks an empty alternative."
                :document.InsertedAt!=null?"This Review was already inserted; it will not be inserted again.":"Ready. Accept returns the reviewed text to its original field.";
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
        note.Text=ReviewCopy.Copy(document,ReviewCopySource.Reviewed,System.Windows.Clipboard.SetText).Message;
    }
    async Task Accept()
    {
        if(IsApplying||!document.CanInsert)return;
        IsApplying=true;((UIElement)Content).IsEnabled=false;
        try
        {
            save(document);
            var result=await accept(document);
            if(result.Success){IsApplying=false;Close();return;}
            Activate();note.Text=result.Message;note.SetResourceReference(TextBlock.ForegroundProperty,"Warning");
        }
        catch(Exception){Activate();note.Text="Insertion did not finish. Your Review is preserved; check the destination before retrying.";}
        finally{IsApplying=false;((UIElement)Content).IsEnabled=true;}
    }
    public void ShowStatus(string message)
    {note.Text=message;note.SetResourceReference(TextBlock.ForegroundProperty,"Warning");}
    void Dismiss()
    {
        if(MessageBox.Show(this,"Remove this pending Review? A saved recording, if available, remains in History.",
            "Dismiss Review",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        dismiss(document.Id);Close();
    }
}
