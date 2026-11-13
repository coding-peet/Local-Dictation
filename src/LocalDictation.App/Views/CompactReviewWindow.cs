using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LocalDictation.App.Views;

/// <summary>Activating phrase choices and editing, separate from the focus-neutral recorder.</summary>
public sealed class CompactReviewWindow : Window
{
    readonly AppController controller;
    readonly CompactReviewPanel panel;
    readonly Button insert;
    readonly TextBlock status=Ui.Text("",12);
    public CompactReviewWindow(AppController controller)
    {
        this.controller=controller;Title="Review · Local Dictation";
        Width=560;MinWidth=400;SizeToContent=SizeToContent.Height;MaxHeight=740;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new StackPanel{Margin=new(24)};
        root.Children.Add(Ui.Text("Review your dictation",24));
        root.Children.Add(Ui.Text("Choose the wording you want to keep, or edit your text.",14));
        panel=new CompactReviewPanel(controller);root.Children.Add(panel);
        insert=Ui.Button("Insert",()=>_=controller.ApplyBannerReview(),true);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,16,0,0)};
        actions.Children.Add(Ui.Button("Close",Close));actions.Children.Add(insert);root.Children.Add(actions);
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
        insert.IsEnabled=document is {CanInsert:true}&&!controller.IsApplyingReview&&!controller.IsBusy;
        status.Text=controller.IsApplyingReview?"Inserting…":document==null?"Your pending text is kept in Settings → Pending Review.":"Closing keeps your pending text and choices.";
        if(IsVisible&&(document==null||document.InsertedAt!=null)&&!controller.IsApplyingReview)Close();
    }
}
