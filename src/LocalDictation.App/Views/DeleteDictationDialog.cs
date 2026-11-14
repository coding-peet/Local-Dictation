using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

/// <summary>A themed decision only. The caller owns any subsequent deletion.</summary>
public sealed class DeleteDictationDialog : Window
{
    readonly Button cancel,delete;
    bool finished,showingModal;
    internal bool Confirmed {get;private set;}

    public DeleteDictationDialog(DictationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Title="Delete dictation";Width=440;MinWidth=320;SizeToContent=SizeToContent.Height;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        AllowsTransparency=true;Background=Brushes.Transparent;BorderThickness=new(0);
        SetResourceReference(FontFamilyProperty,"MaterialDesignFont");FontSize=14;UseLayoutRounding=true;
        AutomationProperties.SetName(this,"Delete dictation confirmation");

        // Keep rounded transparent corners and an adorner layer for keyboard focus.
        var decorator=new FrameworkElementFactory(typeof(AdornerDecorator));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty,"Content");decorator.AppendChild(presenter);
        Template=new ControlTemplate(typeof(Window)){VisualTree=decorator};

        var content=new StackPanel();
        content.Children.Add(Ui.Text("Delete dictation?",24));
        var identity=Ui.Text(item.Document.Created.LocalDateTime.ToString("g",CultureInfo.CurrentCulture),12);
        identity.SetResourceReference(TextBlock.ForegroundProperty,"Muted");identity.Margin=new(0,0,0,16);
        content.Children.Add(identity);
        var explanation=Ui.Text("The recording, transcript and saved review choices will be permanently deleted. This cannot be undone.");
        explanation.Margin=new(0,0,0,24);content.Children.Add(explanation);

        cancel=Ui.Button("Cancel",()=>Finish(false));cancel.IsDefault=true;cancel.IsCancel=true;
        cancel.Margin=new(0,0,8,0);
        delete=Ui.Button("Delete",()=>Finish(true),true);delete.Margin=new(0);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(cancel);actions.Children.Add(delete);content.Children.Add(actions);
        var surface=new Border{Child=content,Padding=new(24),CornerRadius=new(28)};
        surface.SetResourceReference(Border.BackgroundProperty,"SurfaceContainerHigh");
        KeyboardNavigation.SetTabNavigation(surface,KeyboardNavigationMode.Cycle);
        Content=surface;
        FocusManager.SetFocusedElement(this,cancel);
        Loaded+=(_,_)=>{if(IsVisible)cancel.Focus();};
    }

    public static bool Confirm(Window? owner,DictationItem item)
    {
        var dialog=new DeleteDictationDialog(item);
        if(owner!=null)dialog.Owner=owner;
        else dialog.WindowStartupLocation=WindowStartupLocation.CenterScreen;
        dialog.showingModal=true;
        try{return dialog.ShowDialog()==true&&dialog.Confirmed;}
        finally{dialog.showingModal=false;}
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if(e.Handled)return;
        if(e.Key==Key.Escape||e.Key==Key.System&&e.SystemKey==Key.F4)
        {e.Handled=true;Finish(false);}
        else if(e.Key==Key.Enter)
        {
            e.Handled=true;
            if(!e.IsRepeat)Finish(delete.IsKeyboardFocusWithin||ReferenceEquals(FocusManager.GetFocusedElement(this),delete));
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if(!finished){Confirmed=false;finished=true;}
        base.OnClosing(e);
    }

    void Finish(bool confirmed)
    {
        if(finished)return;
        Confirmed=confirmed;finished=true;
        if(showingModal)DialogResult=confirmed;
        else Close();
    }
}