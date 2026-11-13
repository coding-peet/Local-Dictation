using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class AfterRecordingChecks
{
    const BindingFlags Fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,Fields)!.Invoke(target,args);
    static void Require(bool result,string message){if(!result)throw new InvalidOperationException(message);}
    sealed class Delivery:ITextDelivery
    {
        public nint Window=>42;public bool IsConflict=>false;public bool HasTransportFailure=>false;
        public string Reason=>"Synthetic";public string InsertedText=>"";public int Writes,Checks;
        public bool IsOriginalFieldCurrent(CancellationToken token){Checks++;return true;}
        public void Invalidate(){}public void Suspend(){}public bool OwnsProgrammaticFocus(nint root)=>false;
        public bool Apply(string text,CancellationToken token,bool restoreFocus=false){Writes++;return true;}
        public bool Apply(string text,CancellationToken token,DeliveryIntent intent){Writes++;return true;}
    }
    public static void Controller(string output,List<string> checks)
    {
        var doc=new ReviewDocument{RequiresExplicitInsert=true};doc.SetPrimary("Synthetic preview.");
        var owner=ReviewWorkflowChecks.Controller(output,doc);
        Set(owner,"<Models>k__BackingField",owner.Models.Select(m=>m with {LiveSupported=true,LiveReady=true}).ToList());
        var runType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
        var run=RuntimeHelpers.GetUninitializedObject(runType);
        var settings=new Settings{AfterRecording=AfterRecordingAction.PreviewFirst,InsertAtCursor=true};
        Set(run,"Settings",settings);Set(run,"Review",doc);Set(run,"Recording",true);
        Set(run,"CompletionGate",new CompletionDestinationGate());Set(run,"Text",doc.Primary);
        var destination=new Delivery();Set(owner,"active",run);
        Call(owner,"ConfigureDestination",run,destination,"OUTLOOK");
        Require(!Get<bool>(run,"CanDeliver")&&doc.Reason.Contains("Preview first"),"Preview enabled live delivery or showed final-only Outlook instructions.");
        ((Task)Call(owner,"Deliver",run,true)!).GetAwaiter().GetResult();
        Call(owner,"BeginFinish",run);
        ((Task)Call(owner,"WithdrawForReview",run,false)!).GetAwaiter().GetResult();
        Require(destination.Writes==0&&destination.Checks==0,"Preview touched or queried the native destination during live delivery/Finish.");
        Set(owner,"active",null);Set(owner,"<History>k__BackingField",new List<HistoryEntry>());
        doc.Pieces=[new("synthetic-difference",doc.Primary,"Other preview.",true)];Set(owner,"bannerProgress",new CompactReviewProgress(doc));
        owner.KeepBannerWording();
        Require(doc.UnresolvedCount==0&&doc.InsertedAt==null&&destination.Writes==0&&owner.BannerReview==doc,
            "Resolving Preview choices automatically inserted or closed the pending result.");
        Require(JsonSerializer.Deserialize<ReviewDocument>(JsonSerializer.Serialize(doc))!.RequiresExplicitInsert,
            "Reopening a saved Preview lost explicit insertion policy.");
        checks.Add("Preview blocks live/resume/Finish writes and resolving choices retains explicit Insert");
    }
    public static void Render(string theme,string output,List<string> checks)
    {
        var doc=new ReviewDocument();doc.SetPrimary("Synthetic history.");
        var owner=ReviewWorkflowChecks.Controller(output,doc);
        Set(owner,"<Models>k__BackingField",owner.Models.Select(m=>m with {LiveSupported=true,LiveReady=true}).ToList());
        Set(owner,"noHotkey",true);
        var ctor=typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!;
        var window=(SettingsWindow)ctor.Invoke([owner,false]);
        var shell=(MainWindowShell)window.Content;
        window.Content=null;
        var frame=new Border{Child=shell};frame.SetResourceReference(Border.BackgroundProperty,"Canvas");window.Content=frame;
        try
        {
            Set(window,"loading",true);
            var outputMode=Get<ComboBox>(window,"afterRecording");var live=Get<ToggleButton>(window,"live");
            var liveInsert=Get<ToggleButton>(window,"insert");var row=Get<FrameworkElement>(window,"liveInsertRow");
            live.IsChecked=true;liveInsert.IsChecked=true;outputMode.SelectedIndex=1;
            Call(window,"UpdateOutputDependencies");
            Require(!row.IsEnabled&&liveInsert.IsChecked==true&&live.IsChecked==true,"Preview rewrote remembered live insertion or disabled recognition.");
            var before=JsonSerializer.Serialize(owner.Settings);
            Call(window,"OpenSearchSetting",new SettingsSearchEntry("live-insertion","Dictation","Insert live text at the cursor",""));
            Call(window,"OpenSearchSetting",new SettingsSearchEntry("verification-model","Dictation","Verification model",""));
            Require(JsonSerializer.Serialize(owner.Settings)==before&&Get<ListBox>(window,"navigation").SelectedIndex==0,
                "Search changed a setting while navigating to a hidden dependency.");
            outputMode.SelectedIndex=0;Call(window,"UpdateOutputDependencies");
            Require(row.IsEnabled&&liveInsert.IsChecked==true,"Returning to direct output lost the live insertion preference.");
            live.IsChecked=false;Call(window,"UpdateOutputDependencies");
            Require(ReferenceEquals(Call(window,"SearchTarget","live-insertion",liveInsert),live),"Search did not reveal the Live transcription prerequisite in direct output.");
            live.IsChecked=true;
            outputMode.SelectedIndex=1;Call(window,"UpdateOutputDependencies");
            Require(ReferenceEquals(Call(window,"SearchTarget","live-insertion",liveInsert),outputMode),"Search did not reveal the output prerequisite in Preview first.");
            var primary=Get<ComboBox>(window,"primary");
            live.IsEnabled=false;live.IsChecked=false;
            Require(ReferenceEquals(Call(window,"SearchTarget","live-insertion",liveInsert),outputMode),"An unavailable live model took priority over the Preview first output prerequisite.");
            outputMode.SelectedIndex=0;
            Require(ReferenceEquals(Call(window,"SearchTarget","live-transcription",live),primary),"Search did not reveal the Dictation model prerequisite for unsupported live transcription.");
            Require(ReferenceEquals(Call(window,"SearchTarget","live-insertion",liveInsert),primary),"Search routed live insertion to an unavailable Live transcription switch.");
            live.IsEnabled=true;live.IsChecked=true;
            var compatibility=Get<TextBlock>(window,"compatibility");
            var reset=Get<Button>(window,"resetCompatibility");
            reset.Visibility=Visibility.Visible;
            Require(ReferenceEquals(Call(window,"SearchTarget","compatibility",compatibility),reset),"Compatibility search did not target the available reset action.");
            reset.Visibility=Visibility.Collapsed;
            Require(ReferenceEquals(Call(window,"SearchTarget","compatibility",compatibility),compatibility)&&compatibility.Focusable,"Compatibility search did not retain a focusable status when reset was hidden.");
            Require(JsonSerializer.Serialize(owner.Settings)==before,"Prerequisite search routing changed saved settings.");
            checks.Add($"{theme}: unsupported live search reveals model prerequisite and compatibility search targets its visible action");
            outputMode.SelectedIndex=-1;
            Require(!(bool)Call(window,"SaveSelections",false)!&&JsonSerializer.Serialize(owner.Settings)==before,
                "An unknown output selection silently re-enabled direct insertion while saving an unrelated option.");
            outputMode.SelectedIndex=1;
            Set(window,"selectionError","");Set(window,"selectionPending",false);Call(window,"UpdateSaveStatus");
            foreach(var width in new[]{640,1040})
            {
                Layout(frame,width,760);
                Require(shell.Header.ActualHeight<=100&&shell.Navigation.ActualWidth>=150,"Settings search/help expanded the compact header or lost named navigation.");
                Save(frame,Path.Combine(output,$"settings-preview-{theme.ToLowerInvariant()}-{width}.png"));
            }
            Require(new WindowInteropHelper(window).Handle==0,"Settings checks created a native window.");
            checks.Add($"{theme}: real settings Preview dependency, hidden search routes, compact/wide layout without microphone enumeration");
        }
        finally{window.Close();}
    }
    static void Layout(FrameworkElement root,double width,double height)
    {root.Measure(new(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);}
    static void Save(FrameworkElement root,string path)
    {
        var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
