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

namespace LocalDictation.App.Views;

public sealed class BannerWindow : Window
{
    readonly AppController controller;
    readonly TextBlock state=new(),timer=new(),detail=new(),microphone=new(),model=new(),note=new();
    readonly StackPanel shortcut=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
    readonly Border mark,outer;
    readonly StackPanel wave;
    readonly TextBlock grip;
    readonly Button settings;
    (bool HighContrast,System.Windows.Media.Color Window,System.Windows.Media.Color WindowText,System.Windows.Media.Color Highlight,System.Windows.Media.Color HighlightText)? palette;
    readonly Button action,cancel;
    readonly Button previewToggle;
    readonly Border preview;
    readonly Border metadata;
    readonly CompactReviewPanel reviewPanel;
    readonly TextBlock transcript=new(),delivery=new(),previewTitle=new();
    readonly ScrollViewer transcriptScroll;
    bool previewExpanded=true,wasBusy,wasAtCursor;
    bool reviewInteractive;
    readonly System.Windows.Shapes.Path statusIcon;
    readonly Rectangle[] bars=new Rectangle[18];
    bool positioning;
    bool dragging;
    PixelBounds? manualPosition;
    PixelBounds dragBounds;
    BannerMonitor.Point dragStart;
    string? shownShortcut;
    static readonly int[] WaveHeights=[7,12,20,29,16,24,31,18,25,14,21,32,23,15,25,18,11,6];
    static readonly Brush NormalQuiet=Color("#7E8DA4"),NormalBar=Color("#202631"),NormalInk=Color("#F7F9FC"),NormalMuted=Color("#BDC8D9"),NormalRecord=Color("#FF858D");
    static Brush Quiet=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:NormalQuiet;
    static SolidColorBrush Color(string value)=>new((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
    static Brush ContentColor(string normal)=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:Color(normal);
    static Brush SurfaceColor(string normal)=>SystemParameters.HighContrast?SystemColors.WindowBrush:Color(normal);
    static Brush HighlightColor(string normal)=>SystemParameters.HighContrast?SystemColors.HighlightBrush:Color(normal);
    static Brush HighlightTextColor(string normal)=>SystemParameters.HighContrast?SystemColors.HighlightTextBrush:Color(normal);
    static Brush Bar=>SystemParameters.HighContrast?SystemColors.WindowBrush:NormalBar;
    static Brush Ink=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:NormalInk;
    static Brush Muted=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:NormalMuted;
    static Brush Record=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:NormalRecord;
    const string Mic="M10 3a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3M4 11v2a6 6 0 0 0 12 0v-2M10 19v3M7 22h6";
    static System.Windows.Shapes.Path Glyph(string geometry,Brush brush,double size=18)=>new(){Data=Geometry.Parse(geometry),Stroke=brush,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=size,Height=size,Stretch=Stretch.Uniform};
    static Button Button(object content,Action click,bool primary=false)
    {
        var button=Ui.Button("",click);button.Content=content;button.Margin=new(0);button.MinHeight=primary?35:32;
        button.Padding=primary?new(12,7,12,7):new(6);button.MinWidth=primary?74:32;
        button.Background=primary?HighlightColor("#F4F6FA"):Brushes.Transparent;
        button.Foreground=primary?HighlightTextColor("#1F2937"):Muted;button.BorderThickness=new(0);button.FontSize=13;button.FontWeight=FontWeights.SemiBold;
        return button;
    }
    public BannerWindow(AppController controller)
    {
        this.controller=controller;Title="Local Dictation recording";
        Width=614;SizeToContent=SizeToContent.Height;MaxHeight=520;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        AllowsTransparency=true;Background=Brushes.Transparent;Foreground=Ink;
        outer=new Border{Margin=new(17,12,17,18),Background=Bar,CornerRadius=new(17),BorderThickness=new(1),BorderBrush=Color("#27FFFFFF"),Padding=new(17,15,17,12),
            Effect=new DropShadowEffect{BlurRadius=24,ShadowDepth=7,Opacity=.24,Color=Colors.Black}};
        var column=new StackPanel();var main=new Grid();
        foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto})main.ColumnDefinitions.Add(new(){Width=width});
        statusIcon=Glyph(Mic,Record,20);mark=new Border{Width=39,Height=39,CornerRadius=new(12),Background=Color("#383039"),Child=statusIcon,Margin=new(0,0,12,0)};main.Children.Add(mark);
        var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};
        var title=new StackPanel{Orientation=Orientation.Horizontal};state.FontSize=15;state.FontWeight=FontWeights.SemiBold;state.Foreground=Ink;
        timer.FontSize=12;timer.Foreground=Muted;timer.Margin=new(9,2,0,0);title.Children.Add(state);title.Children.Add(timer);
        grip=new TextBlock{Text="⋮⋮",FontSize=11,Foreground=Quiet,Margin=new(8,2,0,0)};title.Children.Add(grip);copy.Children.Add(title);
        detail.FontSize=12;detail.Foreground=Muted;detail.Margin=new(0,2,0,0);detail.TextTrimming=TextTrimming.CharacterEllipsis;detail.TextWrapping=TextWrapping.NoWrap;copy.Children.Add(detail);
        Grid.SetColumn(copy,1);main.Children.Add(copy);
        wave=new StackPanel{Orientation=Orientation.Horizontal,Height=33,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};
        for(var i=0;i<bars.Length;i++){bars[i]=new Rectangle{Width=3,Height=3,RadiusX=1.5,RadiusY=1.5,Fill=Record,Margin=new(0,0,2,0),VerticalAlignment=VerticalAlignment.Center};wave.Children.Add(bars[i]);}
        foreach(var grip in new FrameworkElement[]{mark,copy,wave})MakeDraggable(grip);
        Grid.SetColumn(wave,2);main.Children.Add(wave);
        action=Button("Finish",MainAction,true);System.Windows.Automation.AutomationProperties.SetName(action,"Finish recording or retry");Grid.SetColumn(action,3);main.Children.Add(action);
        cancel=Button(Glyph("M4 4l12 12M16 4L4 16",Muted,14),controller.Cancel);cancel.Margin=new(5,0,0,0);cancel.ToolTip="Cancel or hide";
        System.Windows.Automation.AutomationProperties.SetName(cancel,"Cancel dictation");Grid.SetColumn(cancel,4);main.Children.Add(cancel);column.Children.Add(main);
        var meta=new Grid();
        foreach(var width in new[]{new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto})meta.ColumnDefinitions.Add(new(){Width=width});
        microphone.FontSize=11.5;microphone.Foreground=Muted;microphone.TextTrimming=TextTrimming.CharacterEllipsis;microphone.TextWrapping=TextWrapping.NoWrap;microphone.VerticalAlignment=VerticalAlignment.Center;
        model.FontSize=11.5;model.Foreground=Color("#E5EDF9");model.Margin=new(12,0,12,0);model.VerticalAlignment=VerticalAlignment.Center;
        meta.Children.Add(microphone);Grid.SetColumn(model,1);meta.Children.Add(model);Grid.SetColumn(shortcut,2);meta.Children.Add(shortcut);
        previewToggle=Button("Hide text",()=>{previewExpanded=!previewExpanded;Refresh();});previewToggle.Margin=new(8,0,0,0);
        Grid.SetColumn(previewToggle,3);meta.Children.Add(previewToggle);
        settings=Button(Glyph("M8 2h4l1 3 3 1 3-1 2 4-2 2v3l2 2-2 4-3-1-3 1-1 3H8l-1-3-3-1-3 1-2-4 2-2v-3l-2-2 2-4 3 1 3-1Zm2 7a4 4 0 1 0 0 8a4 4 0 1 0 0-8",Muted,15),controller.ShowSettings);settings.Margin=new(4,0,0,0);settings.ToolTip="Dictation settings";
        System.Windows.Automation.AutomationProperties.SetName(settings,"Dictation settings");Grid.SetColumn(settings,4);meta.Children.Add(settings);
        metadata=new Border{BorderBrush=Color("#17FFFFFF"),BorderThickness=new(0,1,0,0),Padding=new(0,10,0,0),Margin=new(0,12,0,0),Child=meta};column.Children.Add(metadata);
        note.FontSize=12;note.Foreground=Color("#FFC0C4");note.TextWrapping=TextWrapping.Wrap;note.Margin=new(0,9,0,0);note.Visibility=Visibility.Collapsed;column.Children.Add(note);
        var textColumn=new StackPanel();previewTitle.FontSize=11;previewTitle.FontWeight=FontWeights.SemiBold;previewTitle.Foreground=Muted;
        textColumn.Children.Add(previewTitle);
        transcript.FontSize=15;transcript.LineHeight=23;transcript.TextWrapping=TextWrapping.Wrap;transcript.Foreground=Ink;
        transcriptScroll=new ScrollViewer{Content=transcript,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=48,MaxHeight=165,Margin=new(0,10,0,10)};
        textColumn.Children.Add(transcriptScroll);
        delivery.FontSize=12;delivery.LineHeight=18;delivery.TextWrapping=TextWrapping.Wrap;delivery.Foreground=Muted;textColumn.Children.Add(delivery);
        preview=new Border{Background=Color("#18202B"),CornerRadius=new(10),Padding=new(13,11,13,12),Margin=new(0,12,0,0),Child=textColumn};column.Children.Add(preview);
        reviewPanel=new CompactReviewPanel(controller){Visibility=Visibility.Collapsed};column.Children.Add(reviewPanel);
        outer.Child=column;Content=outer;
        SourceInitialized+=(_,_)=>
        {
            var helper=new WindowInteropHelper(this);SetWindowLongPtr(helper.Handle,-20,GetWindowLongPtr(helper.Handle,-20)|0x08000080);
            controller.SetRecordingControlWindow(helper.Handle);
            HwndSource.FromHwnd(helper.Handle).AddHook((nint hwnd,int msg,nint wp,nint lp,ref bool handled)=>
            {if(msg==0x21&&!reviewInteractive){handled=true;return 3;}if(msg==0x2E0)Dispatcher.InvokeAsync(Position);return 0;});
        };
        Loaded+=(_,_)=>Position();SizeChanged+=(_,_)=>{if(IsVisible)Position();};controller.Changed+=Refresh;
    }
    void ApplyAppearance()
    {
        var current=(SystemParameters.HighContrast,SystemColors.WindowColor,SystemColors.WindowTextColor,SystemColors.HighlightColor,SystemColors.HighlightTextColor);
        if(palette==current)return;
        palette=current;
        Foreground=Ink;outer.Background=Bar;outer.BorderBrush=ContentColor("#27FFFFFF");
        outer.Effect=SystemParameters.HighContrast?null:new DropShadowEffect{BlurRadius=24,ShadowDepth=7,Opacity=.24,Color=Colors.Black};
        state.Foreground=Ink;timer.Foreground=Muted;detail.Foreground=Muted;grip.Foreground=Quiet;
        microphone.Foreground=Muted;model.Foreground=ContentColor("#E5EDF9");previewTitle.Foreground=Muted;
        metadata.BorderBrush=ContentColor("#17FFFFFF");preview.Background=SurfaceColor("#18202B");
        action.Background=HighlightColor("#F4F6FA");action.Foreground=HighlightTextColor("#1F2937");
        foreach(var button in new[]{cancel,previewToggle,settings})
        {
            button.Foreground=Muted;
            if(button.Content is System.Windows.Shapes.Path glyph)glyph.Stroke=Muted;
        }
        // Shortcut badges also need fresh semantic brushes when Windows contrast changes.
        shownShortcut=null;
    }

    void MainAction()
    {
        if(controller.IsRecording){controller.Stop();return;}
        if(controller.IsBusy)return;
        if(ClipboardRecovery.HasPending){_=controller.RestoreClipboardAfterInspection();return;}
        if(controller.BannerReview!=null){if(!controller.IsApplyingReview)_=controller.ApplyBannerReview();return;}
        if(controller.StateLabel is "Unavailable" or "Inserted" or "Stopped")controller.Toggle();else controller.OpenLatestReview();
    }

    void SetReviewInteraction(bool interactive)
    {
        var handle=new WindowInteropHelper(this).Handle;
        if(handle==0)return;
        var style=GetWindowLongPtr(handle,-20);
        var next=interactive?style&~(nint)0x08000000:style|(nint)0x08000000;
        if(style!=next)SetWindowLongPtr(handle,-20,next);
        reviewInteractive=interactive;
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
            var result=BannerMonitor.Position(new WindowInteropHelper(this).Handle,controller.BannerAnchorWindow,614,Math.Max(130,ActualHeight),manualPosition);
            if(manualPosition!=null&&result!=null)manualPosition=result;
        }
        finally{positioning=false;}
    }
    public void Refresh()
    {
        ApplyAppearance();
        var review=controller.BannerReview;
        var reviewing=review!=null;
        cancel.IsEnabled=!controller.IsApplyingReview;
        SetReviewInteraction(reviewing&&!controller.IsRecording);
        var failed=controller.StateLabel=="Unavailable";state.Text=failed?"Recording interrupted":controller.StateLabel;
        timer.Text=controller.ElapsedLabel;
        detail.Text=controller.IsRecording?(controller.AtCursor?(controller.LiveTranscription?"Live text · At your cursor":"After Finish · At your cursor"):"Review mode · Text stays here"):
            controller.IsBusy?"Recording stopped · Waiting for the model":failed?"Your microphone or connection needs attention":controller.StateLabel=="Stopped"?"No transcript was returned":"Your transcript is ready";
        microphone.Text="Mic · "+controller.MicrophoneLabel;
        model.Text=controller.ModelLabel.Contains("Turbo")?"KInIT Turbo · SK":controller.ModelLabel.Contains("Large")?"Large v3":controller.ModelLabel;
        if(shownShortcut!=controller.Settings.Shortcut)
        {
            shownShortcut=controller.Settings.Shortcut;shortcut.Children.Clear();
            foreach(var key in shownShortcut.Split('+'))
            {
                if(shortcut.Children.Count>0)shortcut.Children.Add(new TextBlock{Text="+",FontSize=10,Foreground=Muted,Margin=new(4,0,1,0)});
                shortcut.Children.Add(new Border{Background=SurfaceColor("#0CFFFFFF"),BorderBrush=ContentColor("#26FFFFFF"),BorderThickness=new(1),CornerRadius=new(4),Padding=new(4,0,4,0),Margin=new(3,0,0,0),Child=new TextBlock{Text=key,FontSize=10,Foreground=ContentColor("#DBE5F4")}});
            }
        }
        wave.Visibility=reviewing?Visibility.Collapsed:Visibility.Visible;
        for(var i=0;i<bars.Length;i++){bars[i].Height=controller.IsRecording?3+(WaveHeights[i]-3)*Math.Min(1,controller.Level*6):3;bars[i].Fill=controller.IsRecording?Record:Quiet;}
        action.Content=controller.IsRecording?"■  Finish":controller.IsBusy?"Finishing":failed?"Retry":controller.StateLabel is "Inserted" or "Stopped"?"New":"Review";action.IsEnabled=!controller.IsBusy||controller.IsRecording;
        mark.Background=HighlightColor(failed?"#463039":controller.IsRecording?"#383039":"#2C3850");statusIcon.Stroke=HighlightTextColor(failed?"#FFC0C4":controller.IsRecording?"#FF858D":"#C2D7FF");
        statusIcon.Data=Geometry.Parse(failed?"M10 2 1 19h18ZM10 8v5M10 16v.5":controller.IsRecording?Mic:controller.IsBusy?"M10 2a8 8 0 1 0 8 8M10 2v4M18 10h-4":"M3 11l5 5L18 5");
        note.Text=controller.DetailLabel;note.Foreground=failed?ContentColor("#FFC0C4"):Muted;
        note.Visibility=failed||controller.StateLabel=="Stopped"?Visibility.Visible:Visibility.Collapsed;
        if(controller.IsBusy&&(!wasBusy||wasAtCursor&&!controller.AtCursor))previewExpanded=true;
        wasBusy=controller.IsBusy;
        wasAtCursor=controller.AtCursor;
        previewToggle.Content=previewExpanded?"Hide text":"Show text";
        preview.Visibility=previewExpanded?Visibility.Visible:Visibility.Collapsed;
        previewTitle.Text=controller.IsRecording?(controller.LiveTranscription?"LIVE TRANSCRIPT · DRAFT":"TRANSCRIPT AFTER RECORDING"):controller.IsBusy?"FINISHING TRANSCRIPT":"TRANSCRIPT";
        var next=controller.LatestText.Length>0?controller.LatestText:controller.IsRecording
            ?controller.LiveTranscription?"Listening… Text appears after the first speech segment.":"Speak now. This model transcribes after you press Finish."
            :controller.IsBusy?"Waiting for the model…":"No transcript was returned.";
        if(transcript.Text!=next){transcript.Text=next;transcriptScroll.ScrollToEnd();}
        transcript.Foreground=controller.LatestText.Length>0?Ink:Muted;
        delivery.Text=controller.DeliveryMessage;
        delivery.Foreground=controller.AtCursor?Muted:ContentColor("#FFD197");
        metadata.Visibility=reviewing?Visibility.Collapsed:Visibility.Visible;
        if(review!=null)
        {
            state.Text=controller.IsApplyingReview?"Inserting…":"Review";timer.Visibility=Visibility.Collapsed;
            detail.Text=controller.IsApplyingReview?"Returning your wording to the cursor":review!.InsertedAt!=null?"Saved transcript":review.UnresolvedCount>0?"Choose each underlined phrase":"Text ready to insert";
            action.Content="Insert";action.IsEnabled=review.CanInsert&&!controller.IsApplyingReview;
            preview.Visibility=Visibility.Collapsed;
        }
        else timer.Visibility=Visibility.Visible;
        if(!controller.IsBusy&&ClipboardRecovery.HasPending)
        {
            state.Text="Clipboard recovery";detail.Text="Inspect the destination before restoring your clipboard";
            action.Content="Restore clipboard…";action.IsEnabled=controller.CanRestoreClipboard;
            System.Windows.Automation.AutomationProperties.SetName(action,"Restore previous clipboard after inspection");
            preview.Visibility=reviewing?Visibility.Collapsed:Visibility.Visible;previewTitle.Text="CLIPBOARD RECOVERY";
        }
        else System.Windows.Automation.AutomationProperties.SetName(action,"Finish recording or retry");
        reviewPanel.Refresh();
        if(controller.ShowBanner||reviewing){if(!IsVisible)Show();SetReviewInteraction(reviewing&&!controller.IsRecording);Position();}else Hide();
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
}
