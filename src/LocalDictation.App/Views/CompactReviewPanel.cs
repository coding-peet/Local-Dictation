using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

/// <summary>Phrase-choice and direct-edit content hosted by CompactReviewWindow.</summary>
public sealed class CompactReviewPanel : Border
{
    readonly AppController controller;
    readonly TextBlock heading=new(),progress=new(),transcript=new(),status=new();
    readonly ScrollViewer transcriptScroll;
    readonly StackPanel alternatives=new(),editing=new();
    readonly Button first,second,edit,keep,destination,details;
    readonly TextBox custom=new();
    readonly Button saveCustom,cancelCustom,editText,useEdited,exitText,useAlternative;
    readonly TextBox directEditor=new();
    readonly TextBox secondTranscript=new(){IsReadOnly=true,VerticalContentAlignment=VerticalAlignment.Top,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=140};
    readonly StackPanel secondTranscriptPanel=new(){Margin=new(0,12,0,0),Visibility=Visibility.Collapsed};
    readonly WrapPanel directActions=new();
    ReviewDocument? editingDocument;
    bool editingText,updatingDirectText;
    ReviewDocument? shownDocument;
    List<ReviewPiece>? shownPieces;
    Dictionary<string,ReviewChoice>? shownChoices;
    string? shownSelected,shownManual;
    int shownHistory=-1,shownChoiceCount=-1;
    bool editingPhrase,shownInserted,customEnterPending;
    string? editingId;
    static Brush Ink=>Ui.Brush("Ink");
    static Brush Muted=>Ui.Brush("Muted");
    static Brush Pink=>Ui.Brush("Accent");
    static Brush Amber=>Ui.Brush("Warning");
    static Brush Selected=>Ui.Brush("Selected");
    static Brush Surface=>Ui.Brush("SurfaceContainer");
    static Brush FieldSurface=>Ui.Brush("Surface");
    static Brush FieldBorder=>Ui.Brush("InputBorder");

    static Button SmallButton(string text,Action action)
    {
        var button=Ui.Button(text,action);
        button.MinHeight=48;button.Padding=new(12,8,12,8);button.Margin=new(0,0,8,0);
        button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.Foreground=Muted;
        return button;
    }

    Button WordingButton(string accessibleName,string kind)
    {
        var button=Ui.Button("",()=>controller.ChooseBannerPiece(kind));
        button.SetResourceReference(FrameworkElement.StyleProperty,"Md3OutlinedButton");
        button.MinHeight=48;button.Padding=new(16,8,16,8);button.Margin=new(0,0,0,8);
        button.Background=FieldSurface;button.BorderBrush=FieldBorder;button.Foreground=Ink;
        button.HorizontalContentAlignment=HorizontalAlignment.Left;button.HorizontalAlignment=HorizontalAlignment.Stretch;
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
        Margin=new(0,16,0,0);Padding=new(16);SetResourceReference(CornerRadiusProperty,"ShapeMedium");
        Background=Surface;MaxHeight=480;Focusable=true;
        var column=new StackPanel();
        var title=new DockPanel();heading.FontSize=11;heading.FontWeight=FontWeights.SemiBold;heading.Foreground=Muted;
        progress.FontSize=11;progress.Foreground=Muted;progress.HorizontalAlignment=HorizontalAlignment.Right;
        DockPanel.SetDock(progress,Dock.Right);title.Children.Add(progress);title.Children.Add(heading);column.Children.Add(title);
        transcript.FontSize=16;transcript.LineHeight=25;transcript.TextWrapping=TextWrapping.Wrap;transcript.Foreground=Ink;
        System.Windows.Automation.AutomationProperties.SetName(transcript,"Review transcript. Underlined phrases have alternatives. Click plain text or press F2 to edit.");
        transcript.Focusable=true;transcript.Cursor=Cursors.IBeam;
        transcript.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            if(FindHyperlink(e.OriginalSource)!=null&&e.ClickCount<2)return;
            var caret=TranscriptOffset(e.GetPosition(transcript));
            if(BeginTextEdit(caret))e.Handled=true;
        };
        transcript.PreviewTextInput+=(_,e)=>
        {
            if(e.Text.Length==0||e.Text.Any(char.IsControl)||!BeginTextEdit())return;
            directEditor.SelectedText=e.Text;e.Handled=true;
        };
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.F2&&BeginTextEdit()){e.Handled=true;return;}
            if(e.OriginalSource!=directEditor)return;
            // Whole-text editing never accepts on Enter, including key repeats.
            if(e.Key==Key.Enter)e.Handled=true;
            else if(e.Key==Key.Escape)
            {
                e.Handled=true;
                ExitTextEdit();
            }
        };
        transcriptScroll=new ScrollViewer{Content=transcript,MinHeight=40,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,9,0,8)};
        var textSurface=new Grid();textSurface.Children.Add(transcriptScroll);
        directEditor.AcceptsReturn=false;directEditor.AcceptsTab=false;directEditor.TextWrapping=TextWrapping.Wrap;
        directEditor.FontSize=16;directEditor.MinHeight=48;directEditor.MaxHeight=110;
        directEditor.Padding=new(5,4,5,4);directEditor.Margin=new(0,7,0,8);directEditor.VerticalContentAlignment=VerticalAlignment.Top;
        directEditor.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        directEditor.Foreground=Ink;directEditor.Background=FieldSurface;directEditor.BorderBrush=FieldBorder;
        directEditor.SelectionBrush=SystemColors.HighlightBrush;directEditor.SelectionTextBrush=SystemColors.HighlightTextBrush;
        directEditor.Visibility=Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(directEditor,"Edit final transcript");
        System.Windows.Automation.AutomationProperties.SetHelpText(directEditor,"Edits are saved as a draft. Save text keeps the complete result without inserting. Enter never inserts it.");
        directEditor.TextChanged+=(_,_)=>SaveDirectDraft();
        DataObject.AddPastingHandler(directEditor,(_,e)=>
        {
            // Single-paragraph TextBox paste can truncate at a line break.
            // Keep the exact pasted text and explain unsupported controls instead.
            var format=e.DataObject.GetDataPresent(DataFormats.UnicodeText)?DataFormats.UnicodeText:DataFormats.Text;
            if(e.DataObject.GetData(format) is not string value)return;
            var caret=directEditor.SelectionStart;
            directEditor.BeginChange();
            try{directEditor.SelectedText=value;directEditor.CaretIndex=caret+value.Length;}
            finally{directEditor.EndChange();}
            e.CancelCommand();
        });
        textSurface.Children.Add(directEditor);column.Children.Add(textSurface);
        useEdited=Ui.Button("Save text",()=>{if(controller.SaveBannerEditedText())ExitTextEdit();},true);
        useEdited.MinHeight=48;useEdited.Padding=new(16,8,16,8);useEdited.Margin=new(0,0,8,8);
        useEdited.SetResourceReference(Control.BackgroundProperty,"Accent");
        useEdited.SetResourceReference(Control.ForegroundProperty,"OnAccent");
        useEdited.BorderThickness=new(0);useEdited.FontSize=12;useEdited.FontWeight=FontWeights.SemiBold;
        exitText=SmallButton("Done editing",ExitTextEdit);
        directActions.Children.Add(useEdited);directActions.Children.Add(exitText);column.Children.Add(directActions);

        first=WordingButton("First wording","primary");second=WordingButton("Second wording","verification");
        alternatives.Children.Add(first);alternatives.Children.Add(second);column.Children.Add(alternatives);

        custom.AcceptsReturn=false;custom.TextWrapping=TextWrapping.Wrap;custom.MinHeight=38;custom.MaxHeight=65;
        custom.FontSize=14;custom.Foreground=Ink;custom.Background=FieldSurface;custom.BorderBrush=FieldBorder;custom.Padding=new(9,6,9,6);
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
        editText=SmallButton("Edit text",()=>BeginTextEdit());editText.ToolTip="Edit anywhere in the visible transcript (F2)";
        keep=SmallButton("Keep the remaining wording",controller.KeepBannerWording);
        destination=SmallButton("Choose destination",controller.ChooseBannerDestination);
        details=SmallButton("Details",controller.OpenBannerDetails);
        keep.ToolTip="Keep the displayed wording for every remaining phrase";
        details.ToolTip="Open the full editor and recording details";
        var actions=new WrapPanel{Margin=new(0,2,0,0)};
        foreach(var button in new[]{editText,edit,keep,destination,details})actions.Children.Add(button);
        column.Children.Add(actions);
        var secondLabel=Ui.Text("Alternative transcript",12,true);secondLabel.Margin=new(0,0,0,8);
        secondTranscript.SetResourceReference(TextBox.ForegroundProperty,"Ink");secondTranscript.SetResourceReference(TextBox.BackgroundProperty,"Surface");
        secondTranscript.SetResourceReference(TextBox.BorderBrushProperty,"InputBorder");
        secondTranscriptPanel.Children.Add(secondLabel);secondTranscriptPanel.Children.Add(secondTranscript);
        useAlternative=SmallButton("Use this transcript",()=>controller.UseWholeBannerTranscript(ReviewCopySource.Verification));
        secondTranscriptPanel.Children.Add(useAlternative);
        column.Children.Add(secondTranscriptPanel);
        System.Windows.Automation.AutomationProperties.SetName(secondTranscript,"Complete second-model transcript");
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
            FontSize=14,LineHeight=20,MaxHeight=60,Foreground=Ink};
        button.ToolTip=text;
        var order=System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("First",StringComparison.Ordinal)?"First wording":"Second wording";
        System.Windows.Automation.AutomationProperties.SetName(button,order+": "+text);
    }


    static Hyperlink? FindHyperlink(object source)
    {
        for(var current=source as DependencyObject;current!=null;)
        {
            if(current is Hyperlink link)return link;
            current=current is FrameworkContentElement content?content.Parent:LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    int TranscriptOffset(Point point)
    {
        var document=controller.BannerReview;
        var position=transcript.GetPositionFromPoint(point,true);
        if(document==null||position==null)return document?.OutputText.Length??0;
        var offset=new TextRange(transcript.ContentStart,position).Text.Length;
        if(document.ManualFinalDraft!=null)return Math.Min(offset,document.OutputText.Length);
        var rendered=0;var output=0;
        foreach(var piece in document.Pieces)
        {
            var value=Wording(document,piece);
            var length=piece.Different&&value.Length==0?1:value.Length;
            if(offset<=rendered+length)return output+Math.Min(value.Length,Math.Max(0,offset-rendered));
            rendered+=length;output+=value.Length;
        }
        return output;
    }

    bool BeginTextEdit(int? caret=null)
    {
        if(controller.BannerReview is not {} document||document.InsertedAt!=null||controller.IsApplyingReview||controller.IsBusy)return false;
        if(!editingText||editingDocument!=document)
        {
            editingDocument=document;updatingDirectText=true;
            try{directEditor.IsUndoEnabled=false;directEditor.Text=document.OutputText;directEditor.IsUndoEnabled=true;}
            finally{updatingDirectText=false;}
        }
        editingPhrase=false;editingText=true;Refresh();directEditor.Focus();
        if(caret!=null)directEditor.Select(Math.Clamp(caret.Value,0,directEditor.Text.Length),0);
        else if(directEditor.SelectionLength==0)directEditor.CaretIndex=directEditor.Text.Length;
        return true;
    }

    void ExitTextEdit()
    {
        // Leaving the editor never discards its saved draft or inserts it.
        editingText=false;Refresh();transcript.Focus();
    }

    void SaveDirectDraft()
    {
        if(updatingDirectText||!editingText||controller.IsApplyingReview||
            controller.BannerReview is not {} document||document!=editingDocument||document.InsertedAt!=null)return;
        if(document.ManualFinalDraft==null&&directEditor.Text==document.OutputText)return;
        document.ManualFinalDraft=directEditor.Text;controller.SaveReview(document);Refresh();
    }

    void BeginEdit()
    {
        var document=controller.BannerReview;
        var piece=document?.Pieces.FirstOrDefault(p=>p.Id==controller.SelectedReviewPieceId&&p.Different);
        if(document==null||piece==null||editingText||document.ManualFinalDraft!=null||document.InsertedAt!=null)return;
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
        if(document==null){Visibility=Visibility.Collapsed;shownDocument=null;editingDocument=null;editingText=false;return;}
        Visibility=Visibility.Visible;
        secondTranscriptPanel.Visibility=document.HasCompletedVerification?Visibility.Visible:Visibility.Collapsed;
        if(secondTranscript.Text!=document.Verification)secondTranscript.Text=document.Verification;
        var selected=controller.SelectedReviewPieceId;
        var selectedPiece=document.Pieces.FirstOrDefault(p=>p.Different&&p.Id==selected);
        var inserted=document.InsertedAt!=null;
        if(editingId!=selected||shownDocument!=document||inserted)editingPhrase=false;
        var manual=document.ManualFinalDraft;
        if(shownDocument!=document)
        {
            editingText=manual!=null&&!inserted;
            editingDocument=document;updatingDirectText=true;
            try{directEditor.IsUndoEnabled=false;directEditor.Text=document.OutputText;directEditor.IsUndoEnabled=true;}
            finally{updatingDirectText=false;}
        }
        if(inserted)editingText=false;
        if(editingText&&directEditor.Text!=document.OutputText)
        {
            updatingDirectText=true;
            try{directEditor.Text=document.OutputText;directEditor.CaretIndex=directEditor.Text.Length;}
            finally{updatingDirectText=false;}
        }
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
                        Foreground=piece.Id==selected?Ui.Brush("SelectedInk"):resolved?Muted:Pink,TextDecorations=TextDecorations.Underline,
                        Background=piece.Id==selected?Selected:Brushes.Transparent,ToolTip="Choose the correct wording",
                        IsEnabled=!controller.IsApplyingReview
                    };
                    link.Click+=(_,_)=>{if(!editingText)controller.SelectBannerPiece(piece.Id);};transcript.Inlines.Add(link);
                    if(piece.Id==selected)active=link;
                }
                if(active!=null&&!editingText)Dispatcher.BeginInvoke(()=>{if(IsVisible&&!editingText)active.BringIntoView();});
            }
            shownDocument=document;shownPieces=document.Pieces;shownChoices=document.Choices;shownSelected=selected;shownManual=manual;
            shownHistory=document.History.Count;shownChoiceCount=document.Choices.Count;
            shownInserted=inserted;
            if(selectedPiece!=null){SetWording(first,selectedPiece.Primary);SetWording(second,selectedPiece.Verification);}
        }
        var applying=controller.IsApplyingReview||controller.IsBusy;
        var unsaved=controller.IsReviewUnsaved(document.Id);
        heading.Text=inserted?"INSERTED TEXT":editingText?"EDIT YOUR TEXT":manual!=null?"EDITED FINAL TEXT":selectedPiece!=null?"CHOOSE THE CORRECT WORDING":"YOUR TEXT";
        progress.Text=inserted?"Already inserted":controller.IsBusy?"Transcribing…":applying?"Inserting…":unsaved?"Not saved":
            manual!=null&&document.UnresolvedCount==0?"Saved":editingText&&manual!=null?"Draft saved":document.UnresolvedCount==0?"Ready":$"{document.UnresolvedCount} left";
        alternatives.Visibility=selectedPiece!=null&&manual==null&&!editingPhrase&&!editingText&&!inserted?Visibility.Visible:Visibility.Collapsed;
        editing.Visibility=editingPhrase&&manual==null&&!editingText?Visibility.Visible:Visibility.Collapsed;
        edit.Visibility=selectedPiece!=null&&manual==null&&!editingPhrase&&!editingText&&!inserted?Visibility.Visible:Visibility.Collapsed;
        keep.Visibility=document.UnresolvedCount>0&&manual==null&&!editingText&&!inserted?Visibility.Visible:Visibility.Collapsed;
        destination.Visibility=document.CanInsert?Visibility.Visible:Visibility.Collapsed;
        transcriptScroll.Visibility=editingText?Visibility.Collapsed:Visibility.Visible;
        directEditor.Visibility=editingText?Visibility.Visible:Visibility.Collapsed;
        directActions.Visibility=editingText?Visibility.Visible:Visibility.Collapsed;
        editText.Visibility=!editingText&&!inserted?Visibility.Visible:Visibility.Collapsed;
        exitText.Visibility=Visibility.Visible;
        foreach(var button in new[]{first,second,editText,edit,keep,destination,saveCustom,cancelCustom,exitText})button.IsEnabled=!applying&&!inserted;
        details.IsEnabled=!applying;
        custom.IsEnabled=!applying&&!inserted;
        directEditor.IsReadOnly=applying||inserted;
        useEdited.IsEnabled=!applying&&!inserted&&manual!=null;
        useAlternative.IsEnabled=!applying&&!inserted&&document.HasCompletedVerification;
        // Updating only enabled state must not rebuild the inline transcript or steal keyboard focus.
        foreach(var link in transcript.Inlines.OfType<Hyperlink>())link.IsEnabled=!applying&&!inserted;
        status.Text=inserted?unsaved?"This text was inserted, but its receipt could not be saved. Keep Review open and do not insert it again.":"This text was already inserted. Details keeps its recording information and original choices available.":
            unsaved?"Protected saving failed. Your edits are in memory only. Keep Review open and retry saving before inserting.":
            editingText&&manual?.Any(char.IsControl)==true?"Remove line breaks or control characters before inserting. Your exact edited text is saved here.":
            editingText&&manual!=null?"Your draft is saved. Save text keeps the complete result here. Insert is a separate action.":
            editingText?"Edit anywhere in your text. Enter never inserts it. Back to choices keeps the comparison unchanged.":
            manual!=null?"Saved. Click the text or press F2 to edit it. Insert when ready.":document.Reason;
        status.Foreground=unsaved||editingText&&manual?.Any(char.IsControl)==true?Amber:Muted;
    }
}
