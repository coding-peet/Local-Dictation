using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LocalDictation.App.Services;

static class ReviewActivationChecks
{
    const long NoActivate=0x08000000,ToolWindow=0x80;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern nint GetStyle(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern nint SetStyle(nint window,int index,nint style);
    [DllImport("user32.dll")]static extern nint SendMessage(nint window,int message,nint wp,nint lp);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static void Run(string output)
    {
        var checks=new List<string>();var clicks=0;
        var insert=new Button{Content="Insert",Width=100,Height=40,HorizontalAlignment=HorizontalAlignment.Left};
        var other=new Button{Content="Edit",Width=100,Height=40,HorizontalAlignment=HorizontalAlignment.Right};
        var root=new Grid{Margin=new(20)};root.Children.Add(insert);root.Children.Add(other);
        var window=new Window{Content=root,Width=400,Height=180,ShowActivated=false,ShowInTaskbar=false,Opacity=0,WindowStartupLocation=WindowStartupLocation.Manual,Left=-30000,Top=-30000};
        SelectedCaretInsertActivation.Attach(window,insert);insert.Click+=(_,_)=>clicks++;
        var foreground=WindowsTextDelivery.ForegroundWindow;
        try
        {
            window.Show();window.UpdateLayout();var handle=new WindowInteropHelper(window).Handle;
            SetStyle(handle,-20,GetStyle(handle,-20)|(nint)ToolWindow);
            var baseline=(long)GetStyle(handle,-20);
            Require((baseline&NoActivate)==0,"Fixture unexpectedly starts in no-activate mode.");
            void Hit(Button button)
            {
                var p=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
                var packed=((long)(ushort)(short)p.Y<<16)|(ushort)(short)p.X;
                SendMessage(handle,0x84,0,(nint)packed);
            }
            bool Protected()=>((long)GetStyle(handle,-20)&NoActivate)!=0;
            Hit(insert);Require(Protected(),"Insert native hit-test did not preserve the external target before activation.");checks.Add("external Insert hit applies no-activate before mouse activation");
            Hit(other);Require(!Protected(),"Editing control inherited Insert no-activate mode.");checks.Add("other Review controls retain normal activation");
            Hit(insert);insert.IsEnabled=false;Require(!Protected(),"Disabled Insert retained no-activate mode.");checks.Add("disabled Insert releases temporary mode");
            insert.IsEnabled=true;Hit(insert);insert.Visibility=Visibility.Collapsed;window.UpdateLayout();
            Require(!Protected(),"Hidden Insert retained no-activate mode.");checks.Add("hidden Insert does not protect unrelated controls");
            insert.Visibility=Visibility.Visible;window.UpdateLayout();Hit(insert);insert.IsHitTestVisible=false;
            Require(!Protected(),"Non-hit-testable Insert retained no-activate mode.");checks.Add("non-hit-testable Insert releases temporary mode");
            Require(((long)GetStyle(handle,-20)&~NoActivate)==(baseline&~NoActivate),"Activation handling overwrote unrelated native window styles.");checks.Add("unrelated native styles preserved");
            Require(WindowsTextDelivery.ForegroundWindow==foreground&&clicks==0,"Style checks activated a window or dispatched a click.");checks.Add("no foreground change or click dispatch");
            var inherited=new Window{Content=new Button{Content="Inherited"},ShowActivated=false,ShowInTaskbar=false,Opacity=0,Left=-30000,Top=-30000,Width=200,Height=100};
            try
            {
                var inheritedHandle=new WindowInteropHelper(inherited).EnsureHandle();SetStyle(inheritedHandle,-20,GetStyle(inheritedHandle,-20)|(nint)NoActivate);
                SelectedCaretInsertActivation.Attach(inherited,(Button)inherited.Content);inherited.Show();inherited.UpdateLayout();
                ((Button)inherited.Content).IsEnabled=false;
                Require(((long)GetStyle(inheritedHandle,-20)&NoActivate)!=0,"Existing no-activate style was removed.");checks.Add("existing no-activate style retained");
            }
            finally{inherited.Close();}
            File.WriteAllText(Path.Combine(output,"native-style-checks.json"),JsonSerializer.Serialize(new{Checks=checks.Count,Passed=checks,NoPaste=true,NoClipboard=true,NoInputInjection=true},new JsonSerializerOptions{WriteIndented=true}));
            System.Console.WriteLine("Native activation checks passed: "+checks.Count);
        }
        finally{window.Close();}
    }
}
