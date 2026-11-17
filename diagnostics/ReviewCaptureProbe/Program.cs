using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class Program
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static string report="";
    static readonly object gate=new();
    static readonly List<object> events=[];
    static Window target=null!,review=null!;
    static nint targetHandle;
    static int targetProcess;
    static string outputDirectory="";
    static bool closing,noActivateStyle;
    static int captures;
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint process);
    static void Set(object value,string field,object? content)=>value.GetType().GetField(field,Fields)!.SetValue(value,content);
    static void Write(string phase,object? detail=null)
    {
        lock(gate)
        {
            var foreground=WindowsTextDelivery.ForegroundWindow;GetWindowThreadProcessId(foreground,out var foregroundPid);
            events.Add(new{phase,time=DateTimeOffset.Now,foreground=(long)foreground,foregroundPid,detail});
            var json=JsonSerializer.Serialize(new{CaptureOnly=true,NoNativePaste=true,NoClipboardAccess=true,NoMicrophone=true,NoApi=true,NoActivateReviewStyle=noActivateStyle,ExpectedProcess=targetProcess,ReviewProcess=Environment.ProcessId,ExpectedWindow=(long)targetHandle,Captures=captures,Events=events.ToArray()},new JsonSerializerOptions{WriteIndented=true});
            File.WriteAllText(report+".tmp",json);File.Move(report+".tmp",report,true);
        }
    }
    sealed class CaptureOnlyDelivery:ITextDelivery
    {
        public nint Window=>targetHandle;public bool IsConflict{get;private set;}public bool HasTransportFailure=>false;
        public string Reason=>"Capture-only diagnostic; no paste was attempted.";public string InsertedText=>"";
        public bool IsOriginalFieldCurrent(CancellationToken token)=>!IsConflict;
        public void Invalidate()=>IsConflict=true;public void Suspend(){}public bool OwnsProgrammaticFocus(nint window)=>false;
        public bool Apply(string text,CancellationToken token,bool restoreFocus=false){Write("paste-blocked");File.WriteAllText(Path.Combine(outputDirectory,"capture-finished"),"");return false;}
        public bool Apply(string text,CancellationToken token,DeliveryIntent intent)=>Apply(text,token,intent.RestoreFocus);
    }
    [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct GuiInfo{public int Size;public uint Flags;public nint Active,Focus,Capture,MenuOwner,MoveSize,Caret;public NativeRect CaretRect;}
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    [DllImport("user32.dll",SetLastError=true)]static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
    static object OwnFocus()=>new{reviewActive=review?.IsActive,focusedType=Keyboard.FocusedElement?.GetType().Name,
        focusedEnabled=(Keyboard.FocusedElement as UIElement)?.IsEnabled};
    static void RunTarget(System.Windows.Application app,string output)
    {
        var field=new TextBox{Text="Prepared target field. Nothing will be pasted.",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=18,Padding=new(12),VerticalContentAlignment=VerticalAlignment.Top};
        AutomationProperties.SetName(field,"Prepared target field");
        var root=new DockPanel{Margin=new(20)};
        var instructions=new TextBlock{Text="Click this prepared field, then Insert in the test Review.\nSeparate-process capture only: no paste, clipboard, microphone or API.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};
        DockPanel.SetDock(instructions,Dock.Top);root.Children.Add(instructions);
        var close=new Button{Content="Close capture test",Height=42,Margin=new(0,12,0,0)};AutomationProperties.SetName(close,"Close capture test");
        DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);root.Children.Add(field);
        target=new Window{Title="Review capture test — target",Content=root,Width=600,Height=520,Left=40,Top=80,Topmost=true,WindowStartupLocation=WindowStartupLocation.Manual};
        void CloseTarget(){File.WriteAllText(Path.Combine(output,"close.request"),"");target.Close();app.Shutdown();}
        close.Click+=(_,_)=>CloseTarget();
        var end=DateTimeOffset.Now.AddMinutes(10);var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=(_,_)=>{if(File.Exists(Path.Combine(output,"close.request"))||DateTimeOffset.Now>=end){timer.Stop();CloseTarget();}};timer.Start();
        target.Show();var handle=new WindowInteropHelper(target).Handle;
        var targetEvents=new List<object>();
        void TargetWrite(string phase){targetEvents.Add(new{phase,time=DateTimeOffset.Now,foreground=(long)WindowsTextDelivery.ForegroundWindow,focused=field.IsKeyboardFocused,active=target.IsActive});File.WriteAllText(Path.Combine(output,"target-events.json"),JsonSerializer.Serialize(targetEvents,new JsonSerializerOptions{WriteIndented=true}));}
        field.GotKeyboardFocus+=(_,_)=>TargetWrite("target-field-focused");
        field.LostKeyboardFocus+=(_,_)=>TargetWrite("target-field-lost-focus");
        target.Activated+=(_,_)=>TargetWrite("target-activated");
        target.Deactivated+=(_,_)=>TargetWrite("target-deactivated");
        HwndSource.FromHwnd(handle).AddHook((nint hwnd,int msg,nint wp,nint lp,ref bool handled)=>{if(msg is 0x21 or 0x6 or 0x7 or 0x8)TargetWrite("native-"+msg.ToString("x"));return 0;});
        File.WriteAllText(Path.Combine(output,"target-ready.json.tmp"),JsonSerializer.Serialize(new{Process=Environment.ProcessId,Window=(long)handle}));
        File.Move(Path.Combine(output,"target-ready.json.tmp"),Path.Combine(output,"target-ready.json"),true);
        app.Run();
    }

    [STAThread]static void Main(string[] args)
    {
        if(args.Length!=2||args[0] is not ("--output" or "--target" or "--noactivate" or "--style-check"))throw new ArgumentException("Use --output UNIQUE_TEST_DIRECTORY");
        var targetOnly=args[0]=="--target";noActivateStyle=args[0]=="--noactivate";
        var output=Path.GetFullPath(args[1]);
        if(!targetOnly&&Directory.Exists(output))throw new InvalidOperationException("Use a fresh test directory.");
        Directory.CreateDirectory(output);outputDirectory=output;
        report=Path.Combine(output,"capture-report.json");
        Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",Path.Combine(output,"synthetic-home"));
        Environment.SetEnvironmentVariable("LOCAL_DICTATION_DELIVERY_DIAGNOSTICS",null);
        var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
            <md:BundledTheme xmlns:md="clr-namespace:MaterialDesignThemes.Wpf;assembly=MaterialDesignThemes.Wpf" BaseTheme="Light" PrimaryColor="DeepPurple" SecondaryColor="Pink"/>
            """));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")});
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/LocalDictation;component/Theme.xaml")});
        ThemeManager.Apply("Light");
        if(args[0]=="--style-check"){ReviewActivationChecks.Run(output);app.Shutdown();return;}
        if(targetOnly){RunTarget(app,output);return;}
        using var worker=new AutomationWorker();
        var doc=new ReviewDocument{PrimaryModel="first",Language="en",RequiresExplicitInsert=true};doc.SetPrimary("Prepared synthetic dictation. Capture only; no paste.");
        var helper=typeof(ReviewResizeChecks).Assembly.GetType("ReviewWorkflowChecks")!;
        var controller=(AppController)helper.GetMethod("Controller",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[output,doc,worker])!;
        Set(controller,"bannerProgress",new CompactReviewProgress(doc));
        controller.Changed+=()=>Write("before-refresh",OwnFocus());
        var banner=new BannerWindow(controller);Set(controller,"banner",banner);
        Set(controller,"insertionForegroundWindow",(Func<nint>)(()=>WindowsTextDelivery.ForegroundWindow));
        Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(token=>
        {
            Interlocked.Increment(ref captures);
            try
            {
                static object Shape()
                {
                    var element=AutomationElement.FocusedElement;
                    var gui=new GuiInfo{Size=Marshal.SizeOf<GuiInfo>()};
                    var thread=GetWindowThreadProcessId(targetHandle,out _);var nativeOk=GetGUIThreadInfo(thread,ref gui);
                    return new{focusedPid=element?.Current.ProcessId,controlType=element?.Current.ControlType.Id,
                        uiaNativeWindow=element?.Current.NativeWindowHandle,nativeOk,active=(long)gui.Active,focus=(long)gui.Focus,capture=(long)gui.Capture,caret=(long)gui.Caret};
                }
                Write("capture-immediate",Shape());
                Thread.Sleep(200);token.ThrowIfCancellationRequested();
                Write("capture-after-200ms",Shape());
                // Production capture may read only this exact test-owned field. Never Apply it.
                var element=AutomationElement.FocusedElement;
                if(WindowsTextDelivery.ForegroundWindow!=targetHandle||element?.Current.ProcessId!=targetProcess||element.Current.ControlType!=ControlType.Edit)
                    throw new InvalidOperationException("Select the test-owned target field.");
                var actual=WindowsTextDelivery.Capture(token);
                Write("production-capture",new{window=(long)actual.Window,conflict=actual.IsConflict});actual.Invalidate();
                return new CaptureOnlyDelivery();
            }
            finally{File.WriteAllText(Path.Combine(outputDirectory,"capture-finished"),"");}
        }));
        var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--target");start.ArgumentList.Add(output);
        using var child=System.Diagnostics.Process.Start(start)!;targetProcess=child.Id;
        var readyPath=Path.Combine(output,"target-ready.json");var watch=System.Diagnostics.Stopwatch.StartNew();
        while(!File.Exists(readyPath)&&!child.HasExited&&watch.Elapsed<TimeSpan.FromSeconds(12))Thread.Sleep(40);
        if(!File.Exists(readyPath))throw new InvalidOperationException("Test-owned target did not start.");
        using(var json=JsonDocument.Parse(File.ReadAllText(readyPath)))
        {
            if(json.RootElement.GetProperty("Process").GetInt32()!=targetProcess)throw new InvalidOperationException("Target ownership mismatch.");
            targetHandle=(nint)json.RootElement.GetProperty("Window").GetInt64();
        }
        review=new CompactReviewWindow(controller){Title="Review capture test — Review",Left=680,Top=80,WindowStartupLocation=WindowStartupLocation.Manual};
        review.SourceInitialized+=(_,_)=>
        {
            var handle=new WindowInteropHelper(review).Handle;
            if(noActivateStyle)SetWindowLongPtr(handle,-20,GetWindowLongPtr(handle,-20)|0x08000000);
            HwndSource.FromHwnd(handle).AddHook((nint hwnd,int msg,nint wp,nint lp,ref bool handled)=>{if(msg is 0x21 or 0x6 or 0x7 or 0x8)Write("review-native-"+msg.ToString("x"),OwnFocus());return 0;});
        };
        review.Activated+=(_,_)=>Write("review-activated",OwnFocus());review.Deactivated+=(_,_)=>Write("review-deactivated",OwnFocus());
        controller.Changed+=()=>Write("after-refresh",OwnFocus());
        var insert=(Button)typeof(CompactReviewWindow).GetField("insert",Fields)!.GetValue(review)!;
        insert.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler((_,_)=>Write("insert-preview-down",new{active=review.IsActive,captured=insert.IsMouseCaptured})),true);
        insert.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler((_,_)=>Write("insert-preview-up",new{active=review.IsActive,captured=insert.IsMouseCaptured})),true);
        insert.LostMouseCapture+=(_,_)=>Write("insert-lost-capture",new{active=review.IsActive});
        void Finish(){if(closing)return;closing=true;Write("closed");File.WriteAllText(Path.Combine(output,"close.request"),"");review.Close();banner.Close();app.Shutdown();}
        var watchEnd=DateTimeOffset.Now.AddMinutes(10);
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=(_,_)=>{if(File.Exists(Path.Combine(output,"close.request"))||File.Exists(Path.Combine(output,"capture-finished"))||DateTimeOffset.Now>=watchEnd){timer.Stop();Finish();}};timer.Start();
        review.Show();
        var panel=(CompactReviewPanel)typeof(CompactReviewWindow).GetField("panel",Fields)!.GetValue(review)!;
        var edit=(Button)typeof(CompactReviewPanel).GetField("editText",Fields)!.GetValue(panel)!;
        if(!noActivateStyle)edit.Focus();
        Write("ready",OwnFocus());
        app.Run();
    }
}
