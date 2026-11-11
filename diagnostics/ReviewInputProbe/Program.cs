using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

internal static class Program
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly List<CaseResult> results=[];
    static readonly string home=Path.Combine(Path.GetTempPath(),"LocalDictation-ReviewInputProbe-"+Guid.NewGuid().ToString("N"));
    static string? report;

    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length!=2||args[0]!="--report")
        {Console.Error.WriteLine("Usage: ReviewInputProbe --report absolute-json-path");return 2;}
        report=Path.GetFullPath(args[1]);
        var oldHome=Environment.GetEnvironmentVariable("LOCAL_DICTATION_HOME");
        var app=new LocalDictation.App.App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async()=>
        {
            try
            {
                await Case("Enter down/repeats do not resolve unseen phrases",async test=>
                {
                    var button=test.Field<Button>("first");button.Focus();
                    KeyRoute(button,Key.Enter,true);KeyRoute(button,Key.Enter,true);KeyRoute(button,Key.Enter,true);
                    Require(test.Document.Choices.Count==0,"Key down/repeats resolved "+test.Document.Choices.Count+" phrases before release.");
                    KeyRoute(button,Key.Enter,false);await Settle();
                    Require(test.Document.Choices.Count==1,"One Enter release must choose exactly one phrase.");
                    Require(test.Controller.SelectedReviewPieceId=="second","The first Enter gesture must advance only to the second phrase.");
                });
                await Case("Losing wording focus disarms an unfinished Enter gesture",async test=>
                {
                    var button=test.Field<Button>("first");button.Focus();KeyRoute(button,Key.Enter,true);
                    test.Field<Button>("details").Focus();KeyRoute(button,Key.Enter,false);await Settle();
                    Require(test.Document.Choices.Count==0,"A gesture that lost focus must not resolve a phrase.");
                });
                await Case("Compact custom draft survives changing phrases",async test=>
                {
                    test.Edit("  č. 42 😀  ");
                    test.Controller.SelectBannerPiece("second");test.Controller.SelectBannerPiece("first");
                    test.Click("edit");await Settle();
                    Require(test.Field<TextBox>("custom").Text=="  č. 42 😀  ","Changing the selected phrase discarded its custom draft.");
                    Require(test.Document.UnresolvedCount==3,"Saving a custom draft must not accept its phrase.");
                });
                await Case("Compact custom draft survives protected-store reload",async test=>
                {
                    test.Edit("Exact custom wording 😀");await Settle();
                    var restored=new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single();
                    Require(restored.CustomDrafts.GetValueOrDefault("first")=="Exact custom wording 😀","Typed wording was not saved in protected Review storage.");
                    Require(restored.UnresolvedCount==3,"Persisting the draft incorrectly accepted a choice.");
                });
                await Case("Invalid reconnect leaves active and persisted settings unchanged",async test=>
                {
                    test.Controller.Settings.Save();
                    var settings=new SettingsWindow(test.Controller);
                    try
                    {
                        Field<TextBox>(settings,"endpoint").Text="https://invalid.example/";
                        await (Task)settings.GetType().GetMethod("Reconnect",Members)!.Invoke(settings,null)!;
                        Require(test.Controller.Settings.Endpoint=="http://127.0.0.1:48125","Rejected reconnect poisoned the active settings endpoint.");
                        Require(Settings.Load().Endpoint=="http://127.0.0.1:48125","Rejected reconnect changed persisted settings.");
                    }
                    finally{settings.Close();}
                });
                await Case("Held acceptance keys exhaust the release budget without proceeding",_=>
                {
                    var delays=0;
                    Require(!WaitForKeyRelease(()=>true,()=>{},()=>delays++),"Held keys incorrectly passed the release gate.");
                    Require(delays==60,"The release wait did not stop at its bounded 600 ms budget.");
                    return Task.CompletedTask;
                });
                await Case("Released acceptance keys pass only after release",_=>
                {
                    var delays=0;var validations=0;
                    Require(WaitForKeyRelease(()=>delays<3,()=>validations++,()=>delays++),"Released keys never passed the release gate.");
                    Require(delays==3&&validations==4,"The release gate did not validate before and after each wait.");
                    return Task.CompletedTask;
                });
                await Case("Cancellation and changed lease stop the release wait",_=>
                {
                    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
                    var delays=0;var cancelledObserved=false;
                    try{WaitForKeyRelease(()=>true,()=>cancelled.Token.ThrowIfCancellationRequested(),()=>delays++);}
                    catch(OperationCanceledException){cancelledObserved=true;}
                    Require(cancelledObserved&&delays==0,"Cancellation did not stop the gate before waiting.");
                    var leaseValidations=0;var changedObserved=false;
                    try{WaitForKeyRelease(()=>true,()=>{if(++leaseValidations==2)throw new InvalidOperationException("Lease changed");},()=>delays++);}
                    catch(InvalidOperationException ex) when(ex.Message=="Lease changed"){changedObserved=true;}
                    Require(changedObserved&&delays==1,"A changed lease was ignored while awaiting key release.");
                    return Task.CompletedTask;
                });
            }
            catch(Exception ex){results.Add(new("Probe setup",false,ex.ToString()));}
            finally
            {
                Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",oldHome);
                var code=results.All(r=>r.Passed)?0:1;
                var json=JsonSerializer.Serialize(new{Fixture="ReviewInputProbe",SyntheticFieldsOnly=true,NoMicrophone=true,NoApi=true,Passed=code==0,Results=results},new JsonSerializerOptions{WriteIndented=true});
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,json);Console.WriteLine(json);
                // Every path below was generated by this fixture under the OS temporary directory.
                if(Path.GetFullPath(home).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&
                    Path.GetFileName(home).StartsWith("LocalDictation-ReviewInputProbe-",StringComparison.Ordinal)&&Directory.Exists(home))Directory.Delete(home,true);
                Environment.Exit(code);
            }
        }));
        Dispatcher.Run();return 1;
    }

    static async Task Case(string name,Func<TestContext,Task> check)
    {
        try
        {
            using var test=new TestContext(Path.Combine(home,Guid.NewGuid().ToString("N")));
            await check(test);results.Add(new(name,true,"Passed"));
        }
        catch(Exception ex){results.Add(new(name,false,ex.ToString()));}
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static async Task Settle(){await Dispatcher.Yield(DispatcherPriority.ContextIdle);await Task.Delay(25);}
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Members)!.GetValue(target)!;
    static bool WaitForKeyRelease(Func<bool> keysDown,Action validate,Action delay)
    {
        var method=typeof(WindowsTextDelivery).GetMethod("WaitForKeyRelease",BindingFlags.Static|BindingFlags.NonPublic);
        Require(method!=null,"The production acceptance-key release gate is unavailable.");
        try{return (bool)method!.Invoke(null,[keysDown,validate,delay])!;}
        catch(TargetInvocationException ex) when(ex.InnerException!=null)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}
    }

    // Exercise WPF tunnel/bubble routing and ButtonBase's real class handler.
    // No native keyboard input is generated by these compact-panel cases.
    static void KeyRoute(UIElement target,Key key,bool pressed)
    {
        var source=PresentationSource.FromVisual(target)??throw new InvalidOperationException("The fixture control has no presentation source.");
        var args=new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,key)
        {RoutedEvent=pressed?Keyboard.PreviewKeyDownEvent:Keyboard.PreviewKeyUpEvent};
        target.RaiseEvent(args);
        if(!args.Handled)
        {
            args.RoutedEvent=pressed?Keyboard.KeyDownEvent:Keyboard.KeyUpEvent;
            target.RaiseEvent(args);
        }
    }

    sealed record CaseResult(string Name,bool Passed,string Detail);
    sealed class TestContext:IDisposable
    {
        public string Home {get;}
        public AppController Controller {get;}
        public ReviewDocument Document {get;}=new()
        {
            Primary="One two three",Verification="First second third",VerificationModel="synthetic/verifier",VerificationCompleted=true,
            Pieces=[new("first","One","First",true),new("second"," two"," second",true),new("third"," three"," third",true)]
        };
        readonly BannerWindow banner;
        readonly CompactReviewPanel panel;
        public TestContext(string home)
        {
            Home=home;Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",home);
            Controller=new AppController(true);
            foreach(var name in new[]{"timer","reconnectTimer","resumeTimer"})Program.Field<DispatcherTimer>(Controller,name).Stop();
            Controller.SaveReview(Document);Controller.OpenReview(Document);
            banner=Program.Field<BannerWindow>(Controller,"banner");panel=Program.Field<CompactReviewPanel>(banner,"reviewPanel");
            banner.Activate();
        }
        public T Field<T>(string name)=>Program.Field<T>(panel,name);
        public void Click(string name)=>Field<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        public void Edit(string value){Click("edit");Field<TextBox>("custom").Text=value;}
        public void Dispose(){Controller.Dispose();banner.Close();Program.Field<ReviewWindow?>(Controller,"reviewWindow")?.Close();}
    }
}
