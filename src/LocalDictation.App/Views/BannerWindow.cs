using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace LocalDictation.App.Views;

public sealed class BannerWindow : Window
{
    readonly TextBlock state = new(), detail = new(), preview = new();
    readonly ProgressBar meter = new() { Minimum=0,Maximum=1,Height=5,Width=78 };
    readonly Button stop, review;
    readonly AppController controller;
    public BannerWindow(AppController controller)
    {
        this.controller=controller;
        Title="Local Dictation recording";
        Width=590; SizeToContent=SizeToContent.Height; MinHeight=110; MaxHeight=210;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false;
        ShowActivated=false; Topmost=true; Background=new SolidColorBrush(Color.FromRgb(25,34,46));
        Foreground=Brushes.White;
        var column=new StackPanel { Margin=new(18,12,14,12) };
        var header=new DockPanel();
        var actions=Ui.Row(
            stop=Ui.Button("_Stop",()=>controller.Stop()),
            review=Ui.Button("_Review",()=>controller.OpenLatestReview()),
            Ui.Button("×",()=>controller.Cancel()));
        DockPanel.SetDock(actions,Dock.Right); header.Children.Add(actions);
        state.FontSize=15; state.FontWeight=FontWeights.SemiBold; state.VerticalAlignment=VerticalAlignment.Center;
        header.Children.Add(state); column.Children.Add(header);
        detail.FontSize=12; detail.Foreground=new SolidColorBrush(Color.FromRgb(203,213,225)); detail.Margin=new(0,7,0,6);
        column.Children.Add(detail);
        var row=Ui.Row(meter,preview); preview.Margin=new(12,0,0,0); preview.MaxWidth=430;
        preview.TextTrimming=TextTrimming.CharacterEllipsis; preview.TextWrapping=TextWrapping.NoWrap;
        column.Children.Add(row); Content=column;
        SourceInitialized+=(_,_) =>
        {
            var helper=new WindowInteropHelper(this);
            SetWindowLongPtr(helper.Handle,-20,GetWindowLongPtr(helper.Handle,-20)|0x08000080);
            HwndSource.FromHwnd(helper.Handle).AddHook((nint hwnd,int msg,nint wp,nint lp,ref bool handled)=>
            { if(msg==0x21) { handled=true;return 3; } return 0; });
        };
        Loaded+=(_,_)=>Position();
        controller.Changed+=Refresh;
    }
    void Position()
    {
        var area=SystemParameters.WorkArea;
        Width=Math.Min(590,area.Width-32);
        Left=area.Left+(area.Width-Width)/2; Top=area.Bottom-ActualHeight-22;
    }
    public void Refresh()
    {
        state.Text=controller.StateLabel;
        detail.Text=controller.DetailLabel;
        preview.Text=controller.LatestText.Length>0?controller.LatestText:"Speak naturally. Press Ctrl+Shift again to stop.";
        meter.Value=controller.Level;
        stop.IsEnabled=controller.IsRecording;
        review.IsEnabled=controller.Pending.Count>0;
        if(controller.ShowBanner)
        {
            if(!IsVisible)Show();
            Position();
        }
        else Hide();
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
}
