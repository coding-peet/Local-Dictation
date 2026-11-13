using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class ReviewWorkflowChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Property(object target,string name,object? value)=>Set(target,"<"+name+">k__BackingField",value);
    static void Require(bool result,string message){if(!result)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject
    {if(parent is T found)yield return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(parent,i)))yield return child;}
    sealed class Delivery(bool conflict=false,bool uncertain=false):ITextDelivery
    {
        public nint Window=>42;public bool IsConflict{get;private set;}=conflict;public bool HasTransportFailure=>uncertain;
        public string Reason=>"Synthetic destination snapshot";public string InsertedText{get;private set;}="";
        public int Calls;public DeliveryIntent Intent;
        public bool IsOriginalFieldCurrent(CancellationToken token)=>!IsConflict;
        public void Invalidate()=>IsConflict=true;public void Suspend(){}public bool OwnsProgrammaticFocus(nint root)=>false;
        public bool Apply(string text,CancellationToken token,bool restoreFocus=false)=>Apply(text,token,new DeliveryIntent(true,restoreFocus));
        public bool Apply(string text,CancellationToken token,DeliveryIntent intent)
        {token.ThrowIfCancellationRequested();Calls++;Intent=intent;if(IsConflict)return false;InsertedText=text;return true;}
    }
    sealed class Output:IHistoryAudioOutput
    {public TimeSpan Position{get;set;}public bool IsPlaying{get;private set;}public void Play()=>IsPlaying=true;public void Pause()=>IsPlaying=false;public void Stop(){IsPlaying=false;Position=TimeSpan.Zero;}public void Dispose()=>Stop();}
    static ReviewDocument Document()
    {var doc=new ReviewDocument{PrimaryModel="first",Language="en",RequestedLanguage="en"};doc.SetPrimary("Prepared transcript.");return doc;}
    static AppController Controller(string output,ReviewDocument document,AutomationWorker? worker=null)
    {
        var controller=(AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
        Set(controller,"pending",new PendingReviews(new ReviewStore(Path.Combine(output,"synthetic-review",Guid.NewGuid().ToString("N")))));
        Set(controller,"canonicalReviews",new Dictionary<string,ReviewDocument>{{document.Id,document}});
        Set(controller,"destinations",new Dictionary<string,ITextDelivery>());Set(controller,"inserting",new HashSet<string>());
        Property(controller,"History",new List<HistoryEntry>{new(document,4)});
        Property(controller,"Settings",new Settings{Language="en",PrimaryModel="first"});
        Property(controller,"Models",new List<ModelInfo>{new(){Id="first",Name="First model",Languages=["en"],Installed=true,Compatible=true,RuntimeTested=true},new(){Id="second",Name="Second model",Languages=["en"],Installed=true,Compatible=true,RuntimeTested=true}});
        // Avoid a History update path: insertion tests use only the protected pending store.
        if(worker!=null)Property(controller,"History",new List<HistoryEntry>());
        var input=(GlobalInput)RuntimeHelpers.GetUninitializedObject(typeof(GlobalInput));Property(input,"Epoch",new DestinationEpoch());Set(controller,"input",input);
        Set(controller,"insertionForegroundWindow",(Func<nint>)(()=>42));if(worker!=null)Set(controller,"automation",worker);
        return controller;
    }
    public static void Insertion(string output,List<string> checks)
    {
        var context=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(null);
        try{CheckInsertion(output,checks).GetAwaiter().GetResult();}finally{SynchronizationContext.SetSynchronizationContext(context);}
    }
    static async Task CheckInsertion(string output,List<string> checks)
    {
        using(var worker=new AutomationWorker())
        {
            var doc=Document();var controller=Controller(output,doc,worker);var old=new Delivery(conflict:true);var selected=new Delivery();var captures=0;
            Set(controller,"destinations",new Dictionary<string,ITextDelivery>{{doc.Id,old}});
            Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{captures++;return selected;}));
            var result=await controller.InsertReviewAtSelectedCursor(doc);
            Require(result.Success&&captures==1&&selected.Calls==1&&old.Calls==0,"Stale original lease prevented fresh selected-caret insertion.");
            Require(selected.Intent.IsFinal&&!selected.Intent.RestoreFocus&&doc.InsertedAt!=null,"Explicit placement moved focus or lost its final receipt.");
            var again=await controller.InsertReviewAtSelectedCursor(doc);
            Require(again.Success&&captures==1&&selected.Calls==1,"An existing final receipt permitted duplicate insertion.");checks.Add("fresh selected cursor and durable no-duplicate receipt");
        }
        using(var worker=new AutomationWorker())
        {
            var doc=Document();var controller=Controller(output,doc,worker);var selected=new Delivery();
            var input=(GlobalInput)typeof(AppController).GetField("input",Fields)!.GetValue(controller)!;
            Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{input.Epoch.UserInput();return selected;}));
            var result=await controller.InsertReviewAtSelectedCursor(doc);
            Require(!result.Success&&selected.Calls==0&&selected.IsConflict&&doc.InsertedAt==null,"Changed input epoch allowed dispatch to the captured cursor.");checks.Add("selected capture rejects intervening input");
        }
        using(var worker=new AutomationWorker())
        {
            var doc=Document();var controller=Controller(output,doc,worker);var captures=0;
            Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{captures++;throw new InvalidOperationException("Synthetic unsupported field");}));
            var result=await controller.InsertReviewAtSelectedCursor(doc);
            Require(!result.Success&&captures==1&&doc.InsertedAt==null,"Unsupported selected field was accepted.");checks.Add("unsupported selected field keeps transcript");
        }
        using(var worker=new AutomationWorker())using(var release=new ManualResetEventSlim())
        {
            var doc=Document();var controller=Controller(output,doc,worker);var selected=new Delivery();var captures=0;
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(token=>{Interlocked.Increment(ref captures);entered.TrySetResult();release.Wait(token);return selected;}));
            var first=controller.InsertReviewAtSelectedCursor(doc);await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second=await controller.InsertReviewAtSelectedCursor(doc);release.Set();var result=await first;
            Require(result.Success&&!second.Success&&captures==1&&selected.Calls==1,"Double click raced capture or delivery.");checks.Add("selected placement reserves before awaiting capture");
        }
        using(var worker=new AutomationWorker())
        {
            var doc=Document();var controller=Controller(output,doc,worker);var old=new Delivery(conflict:true);var captures=0;
            Set(controller,"destinations",new Dictionary<string,ITextDelivery>{{doc.Id,old}});
            Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{captures++;return new Delivery();}));
            var method=typeof(AppController).GetMethod("AcceptReview",Fields)!;
            var result=await (Task<ReviewInsertionResult>)method.Invoke(controller,[doc,false,(DestinationStamp?)new DestinationStamp(99,0)])!;
            Require(!result.Success&&captures==0&&old.Calls==0&&doc.InsertedAt==null,"Automatic completion adopted arbitrary current focus.");checks.Add("automatic completion retains original finish guard");
        }
        using(var worker=new AutomationWorker())
        {
            var doc=Document();var controller=Controller(output,doc,worker);var captures=0;
            Set(controller,"destinations",new Dictionary<string,ITextDelivery>{{doc.Id,new Delivery(uncertain:true)}});
            Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{captures++;return new Delivery();}));
            Require(!(await controller.InsertReviewAtSelectedCursor(doc)).Success&&captures==0,"Unsettled earlier transport allowed fresh insertion.");checks.Add("uncertain earlier transport prevents replay");
        }
    }
    public static void Render(string theme,string output,List<string> checks)
    {
        var doc=Document();doc.VerificationModel="second";doc.Verification="Prepared second transcript.";
        doc.VerificationCompleted=true;doc.Pieces=[new("0123456789abcdef",doc.Primary,doc.Verification,true)];doc.KeepRemaining();
        var controller=Controller(output,doc);Set(controller,"bannerProgress",new CompactReviewProgress(doc));
        var review=new CompactReviewWindow(controller);
        try
        {
            var root=(FrameworkElement)review.Content;review.Content=null;
            Layout(root,560,720);
            var second=Find<TextBox>(root).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Complete second-model transcript");
            Require(second.IsReadOnly&&second.Text==doc.Verification&&second.ActualHeight>0,"The full second transcript is hidden or editable.");
            var insert=Find<Button>(root).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Insert");
            Require(!insert.Focusable&&insert.IsEnabled,"Review Insert can steal the chosen cursor or is unavailable.");
            Require(Find<Button>(root).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Second transcript…"&&b.IsEnabled),"Saved recording has no second-model action.");
            Require(new WindowInteropHelper(review).Handle==0,"Review rendering created a native window.");
            Save(root,560,720,Path.Combine(output,"review-second-transcript-"+theme+".png"));checks.Add("selected-caret Review and full second transcript "+theme);
        }
        finally{review.Close();}
        var picker=new VerificationWindow(controller.Models.Where(m=>m.Id=="second").ToList(),"second",_=>throw new Exception("Unexpected verification"));
        try
        {
            var root=(FrameworkElement)picker.Content;picker.Content=null;Layout(root,510,300);
            Require(new WindowInteropHelper(picker).Handle==0,"Second-model picker created a native window.");
            Save(root,510,300,Path.Combine(output,"second-model-picker-"+theme+".png"));checks.Add("second model picker "+theme);
        }
        finally{picker.Close();}
        var samples=Enumerable.Range(0,64000).Select(i=>(short)(Math.Sin(i*0.06)*Math.Sin(i/6400.0)*18000)).ToArray();
        var wave=new byte[44+samples.Length*2];"RIFF"u8.CopyTo(wave);BitConverter.GetBytes(wave.Length-8).CopyTo(wave,4);"WAVEfmt "u8.CopyTo(wave.AsSpan(8));
        BitConverter.GetBytes(16).CopyTo(wave,16);BitConverter.GetBytes((short)1).CopyTo(wave,20);BitConverter.GetBytes((short)1).CopyTo(wave,22);BitConverter.GetBytes(16000).CopyTo(wave,24);
        BitConverter.GetBytes(32000).CopyTo(wave,28);BitConverter.GetBytes((short)2).CopyTo(wave,32);BitConverter.GetBytes((short)16).CopyTo(wave,34);"data"u8.CopyTo(wave.AsSpan(36));BitConverter.GetBytes(samples.Length*2).CopyTo(wave,40);Buffer.BlockCopy(samples,0,wave,44,samples.Length*2);
        var outputs=0;using var playback=new HistoryAudioPlayback(_=>{outputs++;return new Output();});playback.Load(wave);playback.Seek(TimeSpan.FromSeconds(1.6));
        using var player=new HistoryAudioPlayerControl(playback);
        player.Measure(new Size(680,double.PositiveInfinity));var playerHeight=(int)Math.Ceiling(player.DesiredSize.Height);
        Require(playerHeight<=112,"History player is still taller than the compact transport.");Layout(player,680,playerHeight);
        Require(outputs==0&&playback.Peaks.Any(p=>p>0),"Rendering opened audio hardware or lost the original-audio waveform.");
        Require(Find<Slider>(player).Single().Maximum==4,"Seek slider duration does not match the recording.");
        Save(player,680,playerHeight,Path.Combine(output,"history-audio-player-"+theme+".png"));checks.Add("silent seekable History waveform player "+theme);
    }
    static void Layout(FrameworkElement root,int width,int height){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
    static void Save(FrameworkElement root,int width,int height,string path)
    {var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);}
}
