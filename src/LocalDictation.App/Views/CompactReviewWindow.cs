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
    readonly TextBlock status=Ui.Text("",12);
    public CompactReviewWindow(AppController controller)
    {
        this.controller=controller;Title="Review · Local Dictation";Topmost=true;
        openedInserted=controller.BannerReview?.InsertedAt!=null;
        Width=560;MinWidth=400;SizeToContent=SizeToContent.Height;MaxHeight=740;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new StackPanel{Margin=new(24)};
        root.Children.Add(Ui.Text("Review your dictation",24));
        root.Children.Add(Ui.Text("Choose the wording you want to keep, or edit your text.",14));
        panel=new CompactReviewPanel(controller);root.Children.Add(panel);
        insert=Ui.Button("Insert",()=>_=controller.ApplyBannerReviewAtSelectedCursor(),true);
        insert.ToolTip="Click the destination cursor, then click Insert. This action keeps that field focused.";
        SelectedCaretInsertActivation.Attach(this,insert);
        verify=Ui.Button("Second transcript…",()=>{if(controller.BannerReview is {} document)controller.ShowReviewVerification(document);});
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,16,0,0)};
        actions.Children.Add(Ui.Button("Close",Close));actions.Children.Add(verify);actions.Children.Add(insert);root.Children.Add(actions);
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");status.Margin=new(0,8,0,0);root.Children.Add(status);
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        controller.Changed+=Refresh;
        Closed+=(_,_)=>controller.Changed-=Refresh;
        Closing+=(_,e)=>{if(controller.IsApplyingReview)e.Cancel=true;};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!controller.IsApplyingReview){e.Handled=true;Close();}};
        Refresh();
    }
    public void Refresh()
    {
        panel.Refresh();var document=controller.BannerReview;
        verify.IsEnabled=document!=null&&controller.CanVerifyReview(document);
        verify.ToolTip=verify.IsEnabled?"Transcribe the original recording with another model":"A saved recording and an eligible second model are required.";
        insert.IsEnabled=document is {CanInsert:true}&&!controller.IsApplyingReview&&!controller.IsBusy;
        status.Text=controller.IsApplyingReview?"Inserting…":document==null?"Your pending text is kept in History.":document.InsertedAt!=null?"Previously inserted. Its original recording can still be compared with another model.":"Select a destination cursor, then click Insert. Closing keeps your text and choices.";
        if(IsVisible&&(document==null||!openedInserted&&document.InsertedAt!=null)&&!controller.IsApplyingReview)Close();
    }
}
