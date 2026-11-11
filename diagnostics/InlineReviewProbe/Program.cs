using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

internal static class Program
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly List<CaseResult> results=[];
    static readonly string root=Path.Combine(Path.GetTempPath(),"LocalDictation-InlineReviewProbe-"+Guid.NewGuid().ToString("N"));
    static string report="";
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length!=2||args[0]!="--report"){Console.Error.WriteLine("Usage: InlineReviewProbe --report absolute-json-path");return 2;}
        report=Path.GetFullPath(args[1]);
        var oldHome=Environment.GetEnvironmentVariable("LOCAL_DICTATION_HOME");
        var app=new LocalDictation.App.App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async()=>
        {
            try
            {
                await Case("Explicit edited approval preserves draft and inserts without implicit choice changes",async t=>
                {
                    t.Document.ManualFinalDraft="  Explicit manual approval 😀.  ";t.Controller.SaveReview(t.Document);
                    var method=typeof(AppController).GetMethod("ApproveBannerEditedText",Members);
                    Require(method!=null,"Explicit edited-text approval API is unavailable.");
                    await (Task)method!.Invoke(t.Controller,null)!;
                    Require(t.Document.UnresolvedCount==0&&t.Document.InsertedAt!=null&&t.Delivery.ApplyCount==1&&t.Delivery.InsertedText=="  Explicit manual approval 😀.  ","Explicit edited approval failed.");
                });
                await Case("F2 opens whole text without creating or approving a draft",async t=>
                {
                    var e=t.Begin();await Settle();
                    Require(e.Text=="  Ahoj  zbytočné spoločné 😀  ","Editor lost exact transcript.");
                    Require(t.Document.ManualFinalDraft==null,"Opening must not create authoritative draft.");
                    Require(t.Document.Choices.Count==0&&t.Delivery.ApplyCount==0,"Opening accepted or inserted text.");
                });
                await Case("Common-word deletion and punctuation preserve sources and earlier choices",async t=>
                {
                    t.Controller.ChooseBannerPiece("verification");t.Panel.Refresh();var history=t.Document.History.Count;
                    var e=t.Begin();e.Select(6,0);e.SelectedText=",";
                    e.Select(e.Text.IndexOf("spoločné ",StringComparison.Ordinal),"spoločné ".Length);e.SelectedText="";await Settle();
                    Require(t.Document.OutputText=="  Ahoj,  nepotrebné 😀  ","Edit changed unrelated Unicode or whitespace.");
                    Require(t.Document.Primary=="  Ahoj  zbytočné spoločné 😀  "&&t.Document.Verification=="  Ahoj  nepotrebné spoločné 🌍  ","Original transcript changed.");
                    Require(t.Document.Choices["first"].Kind=="verification"&&t.Document.Choices.Count==1&&t.Document.History.Count==history,"Typing rewrote choices/history.");
                    Require(t.Delivery.ApplyCount==0,"Typing inserted text.");
                    var saved=new ReviewStore(Path.Combine(t.Home,"data","review")).Load().Single();
                    Require(saved.ManualFinalDraft=="  Ahoj,  nepotrebné 😀  "&&saved.UnresolvedCount==1,"Exact draft was not protected independently of choices.");
                });
                await Case("Clicking normal text opens direct editing while a disputed click keeps alternatives",async t=>
                {
                    var transcript=t.Field<TextBlock>("transcript");
                    var link=transcript.Inlines.OfType<System.Windows.Documents.Hyperlink>().First();
                    link.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));t.Panel.Refresh();
                    Require(!t.Field<TextBox>("directEditor").IsVisible&&t.Field<Button>("first").IsVisible,"Single disputed click stopped showing alternatives.");
                    transcript.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
                    await Settle();Require(t.Field<TextBox>("directEditor").IsVisible,"Normal transcript click did not open same-location editor.");
                    Require(t.Document.ManualFinalDraft==null,"Normal click created a manual draft before an edit.");
                });
                await Case("Typing on the visible transcript appends exact punctuation directly",async t=>
                {
                    var transcript=t.Field<TextBlock>("transcript");transcript.Focus();
                    var composition=new TextComposition(InputManager.Current,transcript,"!");
                    transcript.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,composition){RoutedEvent=TextCompositionManager.PreviewTextInputEvent});
                    await Settle();Require(t.Document.ManualFinalDraft=="  Ahoj  zbytočné spoločné 😀  !","Direct typing lost input or original whitespace.");
                    Require(t.Delivery.ApplyCount==0&&t.Document.UnresolvedCount==2,"Direct typing accepted or inserted text.");
                });
                await Case("Unicode paste keeps line breaks, tabs, emoji, and whitespace exactly",async t=>
                {
                    var e=t.Begin();e.SelectAll();
                    var paste=new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,"  č. 42\r\nnext\t😀  "),false,DataFormats.UnicodeText);
                    e.RaiseEvent(paste);await Settle();
                    Require(e.Text=="  č. 42\r\nnext\t😀  "&&t.Document.ManualFinalDraft=="  č. 42\r\nnext\t😀  ","Native paste silently lost unsupported text.");
                    Require(!t.Field<Button>("useEdited").IsEnabled&&t.Delivery.ApplyCount==0,"Unsupported paste was accepted.");
                });
                await Case("80 ms refresh preserves selection, caret, native undo, and focus",async t=>
                {
                    var e=t.Begin();e.Text="  žluťoučký 😀  ";e.Select(2,5);e.Focus();
                    for(var count=0;count<12;count++){t.Panel.Refresh();await Task.Delay(80);}
                    Require(e.Text=="  žluťoučký 😀  "&&e.SelectionStart==2&&e.SelectionLength==5,"Refresh replaced draft or selection.");
                    Require(e.IsKeyboardFocused,"Refresh stole keyboard focus.");
                    e.SelectedText="Ahoj";t.Panel.Refresh();e.Undo();Require(e.Text=="  žluťoučký 😀  ","Refresh destroyed native Undo.");
                });
                await Case("Enter never approves a whole-text draft",async t=>
                {
                    var e=t.Begin();e.Text="My manual text.";
                    KeyRoute(e,Key.Enter,true);KeyRoute(e,Key.Enter,true);KeyRoute(e,Key.Enter,false);await Settle();
                    Require(t.Document.ManualFinalDraft=="My manual text."&&t.Document.UnresolvedCount==2,"Enter altered or approved draft.");
                    Require(t.Delivery.ApplyCount==0,"Enter inserted text.");
                });
                await Case("Multiline text remains exact with recoverable insertion guidance",async t=>
                {
                    var e=t.Begin();e.Text="Exact\r\nsecond line\t😀  ";await Settle();t.Panel.Refresh();
                    Require(t.Document.ManualFinalDraft=="Exact\r\nsecond line\t😀  ","Control text was truncated/normalized.");
                    var approve=t.Field<Button>("useEdited");Require(!approve.IsEnabled,"Unsupported controls offered for insertion.");
                    Require(t.Field<TextBlock>("status").Text.Contains("line",StringComparison.OrdinalIgnoreCase),"No recoverable line-break guidance.");
                    Require(t.Delivery.ApplyCount==0&&t.Document.UnresolvedCount==2,"Unsupported input accepted/inserted.");
                    e.Text="Repaired text 😀  ";t.Panel.Refresh();Require(approve.IsEnabled,"Repair did not restore approval.");
                });
                await Case("Restart restores exact manual text without accepting or inserting it",async t=>
                {
                    var e=t.Begin();e.Text="  Môj text, 😀.  ";await Settle();var id=t.Document.Id;t.Close();
                    using var restarted=new TestContext(t.Home,false);
                    var doc=restarted.Controller.Pending.Single(d=>d.Id==id);restarted.Controller.OpenReview(doc);restarted.Panel.Refresh();await Settle();
                    var restored=restarted.Field<TextBox>("directEditor");
                    Require(restored.IsVisible&&restored.Text=="  Môj text, 😀.  ","Restart lost authoritative compact draft.");
                    Require(doc.UnresolvedCount==2&&doc.InsertedAt==null&&restarted.Delivery.ApplyCount==0,"Reopening accepted/inserted saved draft.");
                });
                await Case("Use edited text explicitly approves and inserts exact text once",async t=>
                {
                    var e=t.Begin();e.Text="  Final Slovak text, č. 42 😀.  ";t.Click("useEdited");await Until(()=>t.Document.InsertedAt!=null);
                    Require(t.Delivery.ApplyCount==1&&t.Delivery.InsertedText=="  Final Slovak text, č. 42 😀.  ","Approval did not insert exact text once.");
                    Require(t.Document.UnresolvedCount==0&&t.Document.ManualFinalDraft=="  Final Slovak text, č. 42 😀.  ","Approval lost draft or left choices unresolved.");
                    t.Controller.OpenReview(t.Document);t.Panel.Refresh();KeyRoute(t.Panel,Key.F2,true);await Settle();
                    Require(!t.Field<TextBox>("directEditor").IsVisible,"Inserted text opened for editing.");
                    Require(t.Delivery.ApplyCount==1,"Reopening reinserted receipt.");
                });
                await Case("Escape from untouched editor preserves normal phrase auto-advance",async t=>
                {
                    var e=t.Begin();KeyRoute(e,Key.Escape,true);t.Panel.Refresh();
                    Require(t.Document.ManualFinalDraft==null&&t.Field<Button>("first").IsVisible,"Untouched editor lost phrase flow.");
                    t.Click("second");await Settle();
                    Require(t.Document.Choices["first"].Kind=="verification"&&t.Controller.SelectedReviewPieceId=="second","Normal phrase auto-advance failed.");
                });
                await Case("Failed edited approval keeps its message and offers Choose destination",async t=>
                {
                    var e=t.Begin();e.Text="Keep my manual correction.";t.Unbind();t.Click("useEdited");
                    await Until(()=>t.Document.UnresolvedCount==0&&!t.Controller.IsApplyingReview);t.Panel.Refresh();
                    Require(t.Field<TextBlock>("status").Text.Contains("no active destination",StringComparison.OrdinalIgnoreCase),"Insertion failure was hidden by saved-draft help.");
                    Require(t.Field<Button>("destination").IsVisible&&t.Field<Button>("destination").IsEnabled,"Failed manual approval stranded Choose destination.");
                    t.Click("destination");Require(t.Controller.StateLabel=="Choose destination"&&t.Document.ManualFinalDraft=="Keep my manual correction.","Destination action lost the manual draft or did not arm.");
                });
                await Case("Protected save failure stays visible and blocks insertion",async t=>
                {
                    var e=t.Begin();e.Text="Saved before storage fails.";
                    using(var locked=new FileStream(Path.Combine(t.Home,"data","review",t.Document.Id+".review"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                    {
                        e.Text="Keep this unsaved change.";t.Panel.Refresh();
                        Require(t.Controller.IsReviewUnsaved(t.Document.Id),"Fixture did not create a protected save failure.");
                        Require(t.Field<TextBlock>("progress").Text=="Not saved"&&t.Field<TextBlock>("status").Text.Contains("memory only",StringComparison.OrdinalIgnoreCase),"Unsaved text was described as saved.");
                        t.Click("useEdited");await Until(()=>!t.Controller.IsApplyingReview);
                        Require(t.Delivery.ApplyCount==0&&t.Document.InsertedAt==null,"Unsaved manual text was inserted.");
                    }
                    t.Controller.SaveReview(t.Document);t.Panel.Refresh();Require(!t.Controller.IsReviewUnsaved(t.Document.Id),"Saving did not recover after storage was released.");
                    Require(t.Document.ManualFinalDraft=="Keep this unsaved change.","Storage recovery lost the in-memory edit.");
                });
                await Case("Direct editing remains compact and accessible",async t=>
                {
                    var edit=t.Field<Button>("editText");Require(edit.IsVisible&&edit.MinHeight>=27,"Visible edit action unavailable.");
                    var e=t.Begin();e.Text="Ahoj, toto je upravený text. Môžem vymazať slovo alebo doplniť čiarku priamo tu. 😀";await Settle();
                    Require(System.Windows.Automation.AutomationProperties.GetName(e).Length>0,"Editor has no accessible name.");
                    SaveImage(t.Panel,Path.Combine(Path.GetDirectoryName(report)!,"inline-review.png"));
                    Require(t.Panel.ActualHeight<=370,"Editor expanded beyond compact region.");
                });
            }
            catch(Exception ex){results.Add(new("Probe setup",false,ex.ToString()));}
            finally
            {
                Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",oldHome);var code=results.All(r=>r.Passed)?0:1;
                var json=JsonSerializer.Serialize(new{Fixture="InlineReviewProbe",RealWpfController=true,SyntheticFieldsOnly=true,NoNativeInput=true,NoMicrophone=true,NoApi=true,Passed=code==0,Results=results},new JsonSerializerOptions{WriteIndented=true});
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,json);Console.WriteLine(json);
                var path=Path.GetFullPath(root);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(path.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(path).StartsWith("LocalDictation-InlineReviewProbe-",StringComparison.Ordinal)&&Directory.Exists(path))Directory.Delete(path,true);
                Environment.Exit(code);
            }
        }));
        Dispatcher.Run();return 1;
    }
    static async Task Case(string name,Func<TestContext,Task> check)
    {try{using var t=new TestContext(Path.Combine(root,Guid.NewGuid().ToString("N")));await check(t);results.Add(new(name,true,"Passed"));}catch(Exception ex){results.Add(new(name,false,ex.ToString()));}}
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static async Task Settle(){await Dispatcher.Yield(DispatcherPriority.ContextIdle);await Task.Delay(25);}
    static async Task Until(Func<bool> condition)
    {var until=DateTime.UtcNow.AddSeconds(5);while(!condition()){if(DateTime.UtcNow>until)throw new TimeoutException("Expected controller action did not finish.");await Task.Delay(10);}}
    static T Field<T>(object target,string name)
    {var f=target.GetType().GetField(name,Members);Require(f!=null,"Direct editing feature unavailable: "+name);return (T)f!.GetValue(target)!;}
    static void KeyRoute(UIElement target,Key key,bool pressed)
    {
        var source=PresentationSource.FromVisual(target)??throw new InvalidOperationException("Fixture has no presentation source.");
        var args=new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,key){RoutedEvent=pressed?Keyboard.PreviewKeyDownEvent:Keyboard.PreviewKeyUpEvent};
        target.RaiseEvent(args);if(!args.Handled){args.RoutedEvent=pressed?Keyboard.KeyDownEvent:Keyboard.KeyUpEvent;target.RaiseEvent(args);}
    }
    static void SaveImage(FrameworkElement element,string path)
    {
        element.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(element);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(element.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(element),null,new Rect(new Size(element.ActualWidth,element.ActualHeight)));
        bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var stream=File.Create(path);encoder.Save(stream);
    }
    sealed record CaseResult(string Name,bool Passed,string Detail);
    sealed class TestContext:IDisposable
    {
        public string Home {get;}
        public AppController Controller {get;}
        public CompactReviewPanel Panel {get;}
        public FakeDelivery Delivery {get;}=new();
        public ReviewDocument Document {get;}=new()
        {
            Primary="  Ahoj  zbytočné spoločné 😀  ",Verification="  Ahoj  nepotrebné spoločné 🌍  ",VerificationModel="synthetic/verifier",VerificationCompleted=true,
            Pieces=[new("prefix","  Ahoj  ","  Ahoj  ",false),new("first","zbytočné","nepotrebné",true),new("common"," spoločné "," spoločné ",false),new("second","😀","🌍",true),new("suffix","  ","  ",false)]
        };
        readonly BannerWindow banner;bool closed;
        public TestContext(string home,bool open=true)
        {
            Home=home;Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",home);Controller=new AppController(true);
            foreach(var name in new[]{"timer","reconnectTimer","resumeTimer"})Program.Field<DispatcherTimer>(Controller,name).Stop();
            banner=Program.Field<BannerWindow>(Controller,"banner");Panel=Program.Field<CompactReviewPanel>(banner,"reviewPanel");
            if(open){Program.Field<Dictionary<string,ITextDelivery>>(Controller,"destinations")[Document.Id]=Delivery;Controller.SaveReview(Document);Controller.OpenReview(Document);banner.Activate();}
        }
        public T Field<T>(string name)=>Program.Field<T>(Panel,name);
        public void Click(string name)=>Field<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        public TextBox Begin()
        {
            Panel.Focus();KeyRoute(Panel,Key.F2,true);Panel.Refresh();var e=Field<TextBox>("directEditor");
            Require(e.IsVisible,"F2 did not open direct editing in transcript region.");return e;
        }
        public void Unbind()=>Program.Field<Dictionary<string,ITextDelivery>>(Controller,"destinations").Remove(Document.Id);
        public void Close(){if(closed)return;closed=true;Controller.Dispose();banner.Close();Program.Field<ReviewWindow?>(Controller,"reviewWindow")?.Close();}
        public void Dispose()=>Close();
    }
    sealed class FakeDelivery:ITextDelivery
    {
        public nint Window=>0;public bool IsConflict {get;private set;}public string Reason=>"Synthetic destination";
        public int ApplyCount {get;private set;}public string InsertedText {get;private set;}="";
        public void Invalidate()=>IsConflict=true;public void Suspend(){}public bool OwnsProgrammaticFocus(nint root)=>false;
        public bool Apply(string next,CancellationToken token,bool restoreFocus=false){token.ThrowIfCancellationRequested();ApplyCount++;InsertedText=next;return !IsConflict;}
    }
}
