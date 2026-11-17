using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalDictation.App.Services;

namespace LocalDictation.App.Views;

/// <summary>Activating phrase choices and editing, separate from the focus-neutral recorder.</summary>
public sealed class CompactReviewWindow : Window
{
    readonly AppController controller;
    readonly CompactReviewPanel panel;
    readonly bool openedInserted;
    readonly Button insert,verify;
    readonly RetranscriptionPicker transcription;
    readonly ScrollViewer transcriptionScroll;
    readonly TextBlock status=Ui.Text("",12);
    public CompactReviewWindow(AppController controller)
    {
        this.controller=controller;Title="Review · Local Dictation";Topmost=true;
        openedInserted=controller.BannerReview?.InsertedAt!=null;
        Width=620;MinWidth=400;Height=680;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new Grid{Margin=new(24)};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel();
        header.Children.Add(Ui.Text(controller.BannerReview?.IsPrivate==true?"Private dictation · Memory only":"Review your dictation",24));
        header.Children.Add(Ui.Text("Choose the wording you want to keep, or edit your text.",14));
        root.Children.Add(header);
        panel=new CompactReviewPanel(controller);Grid.SetRow(panel,1);root.Children.Add(panel);
        insert=Ui.Button("Insert",()=>_=controller.ApplyBannerReviewAtSelectedCursor(),true);
        insert.ToolTip="Click the destination cursor, then click Insert. This action keeps that field focused.";
        SelectedCaretInsertActivation.Attach(this,insert);
        transcription=new RetranscriptionPicker(
            ()=>controller.BannerReview is {} document?controller.VerificationChoices(document):[],
            model=>controller.BannerReview is {} document?controller.StartSavedVerification(document,model):"Select a saved dictation first.");
        transcriptionScroll=new ScrollViewer{Content=transcription,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Visibility=Visibility.Collapsed};
        System.Windows.Automation.AutomationProperties.SetName(transcriptionScroll,"Choose another transcription model");
        Grid.SetRow(transcriptionScroll,1);root.Children.Add(transcriptionScroll);
        verify=Ui.Button("Transcribe again",transcription.Toggle);
        transcription.ReturnFocusTarget=verify;
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,16,0,0)};
        if(controller.BannerReview?.IsPrivate==true)actions.Children.Add(Ui.Button("Discard private text",()=>{if(controller.BannerReview is {} d)controller.RequestDiscardPrivateReview(d.Id);}));
        actions.Children.Add(Ui.Button("Close",Close));actions.Children.Add(verify);actions.Children.Add(insert);
        var footer=new StackPanel();footer.Children.Add(actions);Grid.SetRow(footer,2);root.Children.Add(footer);
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");status.Margin=new(0,8,0,0);footer.Children.Add(status);
        Content=root;
        transcription.Changed+=Refresh;
        controller.Changed+=Refresh;
        Closed+=(_,_)=>controller.Changed-=Refresh;
        Closing+=(_,e)=>{if(controller.IsApplyingReview)e.Cancel=true;};
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key!=Key.Escape||controller.IsApplyingReview)return;
            e.Handled=true;
            if(!e.IsRepeat&&!transcription.Cancel())Close();
        };
        Refresh();
    }
    public void Refresh()
    {
        panel.Refresh();var document=controller.BannerReview;
        var choosingModel=transcription.Visibility==Visibility.Visible;
        transcriptionScroll.Visibility=choosingModel?Visibility.Visible:Visibility.Collapsed;
        panel.Visibility=choosingModel?Visibility.Collapsed:document==null?Visibility.Collapsed:Visibility.Visible;
        verify.IsEnabled=document!=null&&controller.CanVerifyReview(document);
        transcription.Refresh(verify.IsEnabled);
        verify.ToolTip=verify.IsEnabled?"Transcribe the original recording with another model":"A saved recording and an eligible second model are required.";
        insert.IsEnabled=document is {CanInsert:true}&&!controller.IsApplyingReview&&!controller.IsBusy&&!choosingModel;
        if(controller.IsBusy)status.Text="Transcribing the original recording…";
        else if(choosingModel)status.Text="Choose a model and confirm Transcribe. Your current text is kept.";
        else
        status.Text=controller.IsApplyingReview?"Inserting…":controller.BannerInsertionError is {Length:>0} failure?failure:
            document?.IsPrivate==true?"Private text stays in memory only. Closing the app or discarding loses it.":document==null?"Your pending text is kept in History.":document.InsertedAt!=null?"Previously inserted. Your text and saved Review choices are preserved.":"Select a destination cursor, then click Insert. Closing keeps your text and choices.";
        status.SetResourceReference(TextBlock.ForegroundProperty,controller.BannerInsertionError.Length>0?"Warning":"Muted");
        if(IsVisible&&(document==null||!openedInserted&&document.InsertedAt!=null)&&!controller.IsApplyingReview)Close();
    }
}
