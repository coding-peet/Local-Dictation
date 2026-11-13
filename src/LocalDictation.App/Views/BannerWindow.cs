using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using LocalDictation.App.Services;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

/// <summary>Focus-neutral recording controls. Review and transcript editing have their own window.</summary>
public sealed class BannerWindow : Window
{
    readonly AppController controller;
    readonly TextBlock state=Ui.Text(""),timer=Ui.Text("",12),preview=Ui.Text("",14),model=Ui.Text("",11);
    readonly ScrollViewer previewScroll;
    readonly Button action,cancel,language;
    readonly Border outer,cloud;
    readonly Rectangle[] bars=new Rectangle[7];
    static readonly int[] WaveHeights=[9,18,28,20,32,16,8];
    bool positioning,dragging,showingPreview;
    PixelBounds? manualPosition;
    PixelBounds dragBounds;
    BannerMonitor.Point dragStart;

    public BannerWindow(AppController controller)
    {
        this.controller=controller;Title="Local Dictation recording";
        Width=392;Height=88;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        var exposeTestBanner=Environment.GetEnvironmentVariable("LOCAL_DICTATION_TEST_BANNER_WINDOW")=="1";
        ShowInTaskbar=exposeTestBanner;ShowActivated=false;Topmost=true;
        AllowsTransparency=true;Background=Brushes.Transparent;
        SetResourceReference(FrameworkElement.StyleProperty,"CloudBannerWindow");
        // Blur only the decorative surface; the content and interaction targets stay sharp.
        cloud=new Border{Margin=new(12),Height=64,IsHitTestVisible=false};
        Ui.BindCapsule(cloud);
        cloud.SetResourceReference(Border.BackgroundProperty,"SurfaceContainerHigh");
        outer=new Border{Margin=new(12),Height=64,Padding=new(16,8,8,8),Background=Brushes.Transparent};
        var main=new Grid{Height=48};
        foreach(var width in new[]{new GridLength(40),new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})main.ColumnDefinitions.Add(new(){Width=width});
        var wave=new StackPanel{Orientation=Orientation.Horizontal,Height=36,VerticalAlignment=VerticalAlignment.Center};
        for(var i=0;i<bars.Length;i++)
        {bars[i]=new Rectangle{Width=3,Height=4,RadiusX=1.5,RadiusY=1.5,Margin=new(0,0,2,0),VerticalAlignment=VerticalAlignment.Center};bars[i].SetResourceReference(Shape.FillProperty,"Recording");wave.Children.Add(bars[i]);}
        main.Children.Add(wave);
        var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,8,0)};
        state.FontSize=14;state.FontWeight=FontWeights.Medium;state.TextWrapping=TextWrapping.NoWrap;state.TextTrimming=TextTrimming.CharacterEllipsis;state.Margin=new(0);
        timer.SetResourceReference(TextBlock.ForegroundProperty,"Muted");timer.Margin=new(0,4,0,0);timer.TextWrapping=TextWrapping.NoWrap;timer.TextTrimming=TextTrimming.CharacterEllipsis;
        var headline=new Grid();headline.ColumnDefinitions.Add(new(){Width=GridLength.Auto});headline.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        model.Margin=new(8,0,0,0);model.VerticalAlignment=VerticalAlignment.Center;model.TextAlignment=TextAlignment.Right;
        model.TextWrapping=TextWrapping.NoWrap;model.TextTrimming=TextTrimming.CharacterEllipsis;model.Focusable=false;
        model.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        System.Windows.Automation.AutomationProperties.SetName(model,"Active transcription model");
        headline.Children.Add(state);Grid.SetColumn(model,1);headline.Children.Add(model);
        System.Windows.Automation.AutomationProperties.SetName(timer,"Elapsed time and input language");
        copy.Children.Add(headline);copy.Children.Add(timer);
        language=Ui.Button("Change input language",()=>controller.ShowInputLanguage());
        language.Content=copy;language.Focusable=false;language.Height=48;language.MinWidth=0;language.Padding=new(0);language.Margin=new(0);
        language.HorizontalAlignment=HorizontalAlignment.Stretch;language.HorizontalContentAlignment=HorizontalAlignment.Stretch;language.ToolTip="Change language and reprocess this recording";
        Grid.SetColumn(language,1);main.Children.Add(language);
        MakeDraggable(wave);MakeDraggable(state);MakeDraggable(model);
        action=Ui.IconButton(PackIconKind.Stop,"Finish recording",MainAction);action.Focusable=false;
        Grid.SetColumn(action,2);main.Children.Add(action);
        cancel=Ui.IconButton(PackIconKind.Close,"Cancel dictation or hide result",controller.Cancel);cancel.Focusable=false;
        Grid.SetColumn(cancel,3);main.Children.Add(cancel);
        preview.FontSize=14;preview.LineHeight=20;preview.Margin=new(0);preview.Focusable=false;
        preview.TextWrapping=TextWrapping.Wrap;preview.TextTrimming=TextTrimming.None;
        System.Windows.Automation.AutomationProperties.SetName(preview,"First transcript preview");
        System.Windows.Automation.AutomationProperties.SetHelpText(preview,"Read only while verification runs. The completed text returns to the original cursor after comparison.");
        previewScroll=new ScrollViewer{Content=preview,Height=80,MaxHeight=80,Margin=new(0,8,8,0),
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            Focusable=false,IsTabStop=false,Visibility=Visibility.Collapsed};
        var body=new StackPanel();body.Children.Add(main);body.Children.Add(previewScroll);outer.Child=body;
        var layers=new Grid{Background=Brushes.Transparent,ClipToBounds=true};layers.Children.Add(cloud);layers.Children.Add(outer);Content=layers;
        SourceInitialized+=(_,_)=>
        {
            var helper=new WindowInteropHelper(this);
            var style=GetWindowLongPtr(helper.Handle,-20)|0x08000000; // WS_EX_NOACTIVATE is never removed.
            style=exposeTestBanner?(style&~0x80)|0x40000:style|0x80;
            SetWindowLongPtr(helper.Handle,-20,style);controller.SetRecordingControlWindow(helper.Handle);
            HwndSource.FromHwnd(helper.Handle).AddHook((nint hwnd,int msg,nint wp,nint lp,ref bool handled)=>
            {if(msg==0x21){handled=true;return 3;}if(msg==0x2E0)Dispatcher.InvokeAsync(Position);return 0;});
        };
        Loaded+=(_,_)=>Position();controller.Changed+=Refresh;ThemeManager.Changed+=Appearance;
        Closed+=(_,_)=>{controller.Changed-=Refresh;ThemeManager.Changed-=Appearance;};
        Appearance();
    }
    void Appearance()=>cloud.Effect=new BlurEffect{Radius=10,KernelType=KernelType.Gaussian,RenderingBias=RenderingBias.Quality};
    void MainAction()
    {
        if(controller.IsRecording){controller.Stop();return;}
        if(controller.IsBusy||controller.IsApplyingReview)return;
        if(controller.CanRestoreClipboard){controller.ShowClipboardRecoveryDetails();return;}
        if(controller.BannerReview is {CanInsert:true})_=controller.ApplyBannerReview();
        else if(controller.BannerReview is {} review)controller.OpenReview(review);
    }
    void MakeDraggable(FrameworkElement grip)
    {
        grip.Cursor=Cursors.SizeAll;grip.ToolTip="Drag to move the recording banner";
        if(grip is Panel panel)panel.Background=Brushes.Transparent;
        grip.MouseLeftButtonDown+=(_,e)=>
        {
            var bounds=BannerMonitor.Bounds(new WindowInteropHelper(this).Handle);
            if(bounds==null||!BannerMonitor.GetCursorPos(out dragStart))return;
            dragBounds=bounds.Value;dragging=true;
            if(!grip.CaptureMouse()){dragging=false;return;}
            e.Handled=true;
        };
        grip.MouseMove+=(_,e)=>
        {
            if(!dragging||e.LeftButton!=MouseButtonState.Pressed||!BannerMonitor.GetCursorPos(out var pointer))return;
            var dx=pointer.X-dragStart.X;var dy=pointer.Y-dragStart.Y;
            if(Math.Abs(dx)+Math.Abs(dy)<4)return;
            var handle=new WindowInteropHelper(this).Handle;
            BannerMonitor.Move(handle,dragBounds.Left+dx,dragBounds.Top+dy);
            manualPosition=BannerMonitor.Bounds(handle);
            e.Handled=true;
        };
        grip.MouseLeftButtonUp+=(_,e)=>
        {
            if(!dragging)return;dragging=false;grip.ReleaseMouseCapture();Position();e.Handled=true;
        };
        grip.LostMouseCapture+=(_,_)=>{if(dragging){dragging=false;Position();}};
    }
    void Position()
    {
        if(positioning||dragging)return;positioning=true;
        try
        {
            var result=BannerMonitor.Position(new WindowInteropHelper(this).Handle,controller.BannerAnchorWindow,Width,Height,manualPosition);
            if(manualPosition!=null&&result!=null)manualPosition=result;
        }
        finally{positioning=false;}
    }

    public void Refresh()
    {
        var review=controller.BannerReview;
        var recover=controller.CanRestoreClipboard;
        var previewText=controller.VerificationPreviewText;
        var showPreview=previewText.Length>0;
        if(showPreview!=showingPreview)
        {
            showingPreview=showPreview;
            Width=showPreview?456:392;Height=showPreview?176:88;
            cloud.Height=outer.Height=showPreview?152:64;
            // The expanded transcript surface is a compact MD3 card; controls keep
            // their own true capsule geometry and the decorative edge still fades.
            if(showPreview)cloud.SetResourceReference(Border.CornerRadiusProperty,"ShapeLarge");
            else Ui.BindCapsule(cloud);
        }
        preview.Text=previewText;previewScroll.Visibility=showPreview?Visibility.Visible:Visibility.Collapsed;
        state.Text=recover?"Clipboard recovery":controller.IsRecording?"Recording":controller.IsVerifying?"Verifying…":controller.IsBusy?"Finishing…":review is {CanInsert:true}?"Text ready":review!=null?"Review needed":controller.StateLabel=="Unavailable"?"Interrupted":controller.StateLabel;
        model.Text=controller.BannerModelLabel;model.ToolTip=controller.BannerModelDetails;
        System.Windows.Automation.AutomationProperties.SetHelpText(model,controller.BannerModelDetails);
        timer.Text=recover?"Resolve to continue":showPreview?controller.ElapsedLabel:controller.IsRecording||controller.IsBusy?controller.ElapsedLabel:review is {CanInsert:true}?"Insert when ready":review!=null?"Open Review":"";
        if(!recover&&controller.CanChangeInputLanguage)timer.Text+=" · "+controller.InputLanguageLabel;
        language.IsEnabled=controller.CanChangeInputLanguage&&!recover;
        var insert=!recover&&!controller.IsBusy&&review is {CanInsert:true}&&!controller.IsApplyingReview;
        var pending=!controller.IsBusy&&(review!=null||recover)&&!controller.IsApplyingReview;
        if(pending)
        {
            action.SetResourceReference(FrameworkElement.StyleProperty,"Md3TextButton");action.Content=recover?"Resolve":insert?"Insert":"Review";
            action.Width=double.NaN;action.Height=48;action.MinWidth=56;action.Padding=new(8);action.Margin=new(0);
        }
        else
        {
            action.SetResourceReference(FrameworkElement.StyleProperty,"Md3IconButton");
            action.Content=new PackIcon{Kind=PackIconKind.Stop,Width=20,Height=20};
            action.ClearValue(WidthProperty);action.ClearValue(HeightProperty);action.ClearValue(MinWidthProperty);action.ClearValue(Control.PaddingProperty);action.ClearValue(MarginProperty);
        }
        action.IsEnabled=controller.IsRecording||pending;action.Visibility=controller.IsRecording||controller.IsBusy||pending?Visibility.Visible:Visibility.Collapsed;
        var actionLabel=recover?"Resolve clipboard recovery":insert?"Insert completed dictation":pending?"Open dictation review":"Finish recording";action.ToolTip=actionLabel;
        System.Windows.Automation.AutomationProperties.SetName(action,actionLabel);
        cancel.IsEnabled=!controller.IsApplyingReview;
        for(var i=0;i<bars.Length;i++)bars[i].Height=controller.IsRecording?4+(WaveHeights[i]-4)*Math.Min(1,controller.Level*6):4;
        if(controller.ShowBanner){if(!IsVisible)Show();Position();}else Hide();
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
}
