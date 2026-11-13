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
    static IEnumerable<T> LogicalFind<T>(DependencyObject parent) where T:DependencyObject
    {if(parent is T found)yield return found;foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())foreach(var item in LogicalFind<T>(child))yield return item;}
    sealed class Delivery(bool conflict=false,bool uncertain=false,bool reject=false):ITextDelivery
    {
        public nint Window=>42;public bool IsConflict{get;private set;}=conflict;public bool HasTransportFailure=>uncertain;
        public string Reason=>"Synthetic destination snapshot";public string InsertedText{get;private set;}="";
        public int Calls;public DeliveryIntent Intent;
        public bool IsOriginalFieldCurrent(CancellationToken token)=>!IsConflict;
        public void Invalidate()=>IsConflict=true;public void Suspend(){}public bool OwnsProgrammaticFocus(nint root)=>false;
        public bool Apply(string text,CancellationToken token,bool restoreFocus=false)=>Apply(text,token,new DeliveryIntent(true,restoreFocus));
        public bool Apply(string text,CancellationToken token,DeliveryIntent intent)
        {token.ThrowIfCancellationRequested();Calls++;Intent=intent;if(IsConflict||reject)return false;InsertedText=text;return true;}
    }
    sealed class Output:IHistoryAudioOutput
    {public TimeSpan Position{get;set;}public bool IsPlaying{get;private set;}public void Play()=>IsPlaying=true;public void Pause()=>IsPlaying=false;public void Stop(){IsPlaying=false;Position=TimeSpan.Zero;}public void Dispose()=>Stop();}
    static ReviewDocument Document()
    {var doc=new ReviewDocument{PrimaryModel="first",Language="en",RequestedLanguage="en"};doc.SetPrimary("Prepared transcript.");return doc;}
    internal static AppController Controller(string output,ReviewDocument document,AutomationWorker? worker=null)
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
            var doc=Document();doc.RequiresExplicitInsert=true;
            var controller=Controller(output,doc,worker);var old=new Delivery(conflict:true);var selected=new Delivery();var captures=0;
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
            var selected=new Delivery();
            Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{captures++;return selected;}));
            Require((await controller.InsertReviewAtSelectedCursor(doc)).Success&&captures==1&&selected.Calls==1,
                "An unconfirmed earlier attempt blocked the user's explicit Insert.");
            Require((await controller.InsertReviewAtSelectedCursor(doc)).Success&&captures==1&&selected.Calls==1,
                "The confirmed explicit retry permitted duplicate insertion.");
            checks.Add("explicit retry after uncertain attempt retains no-duplicate receipt");
        }
        using(var worker=new AutomationWorker())
        {
            var doc=Document();var controller=Controller(output,doc,worker);
            var rejected=new Delivery(reject:true);var accepted=new Delivery();var captures=0;
            Set(controller,"bannerProgress",new CompactReviewProgress(doc));
            Set(controller,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>++captures==1?rejected:accepted));
            Require(!(await controller.InsertReviewAtSelectedCursor(doc)).Success&&controller.ShowBanner&&controller.BannerReview==doc&&doc.InsertedAt==null,
                "An unsuccessful Insert hid the pending banner or marked unconfirmed text as inserted.");
            Require((await controller.InsertReviewAtSelectedCursor(doc)).Success&&!controller.ShowBanner&&controller.BannerReview==null&&accepted.Calls==1,
                "A confirmed retry failed to close the pending banner.");
            checks.Add("failed Insert keeps banner until confirmed retry");
        }
        using(var worker=new AutomationWorker())
        {
            var doc=Document();doc.ManualFinalDraft="Exact edited wording.";
            doc.Pieces=[new("difference",doc.Primary,"Other wording.",true)];
            var controller=Controller(output,doc,worker);Set(controller,"bannerProgress",new CompactReviewProgress(doc));
            var reviewDirectory=Path.Combine(output,"saved-edited-text-"+Guid.NewGuid().ToString("N"));
            Set(controller,"pending",new PendingReviews(new ReviewStore(reviewDirectory)));
            var target=new Delivery();Set(controller,"destinations",new Dictionary<string,ITextDelivery>{{doc.Id,target}});
            var save=typeof(AppController).GetMethod("SaveBannerEditedText");
            Require(save!=null,"Review has no explicit Save text action independent of Insert.");
            Require((bool)save!.Invoke(controller,null)!,"Save text failed for a valid protected store.");
            Require(target.Calls==0&&doc.InsertedAt==null&&controller.BannerReview==doc&&doc.UnresolvedCount==0,
                "Save text inserted externally, closed the pending result, or left the edited draft unresolved.");
            var reopened=new ReviewStore(reviewDirectory).Load().Single();
            Require(reopened.OutputText=="Exact edited wording."&&reopened.UnresolvedCount==0&&reopened.InsertedAt==null,
                "Saved text was not persisted for reopening.");
            checks.Add("Save text persists exact edited draft without insertion");
        }
        using(var worker=new AutomationWorker())
        {
            var doc=Document();doc.ManualFinalDraft="Keep this unsaved correction.";
            var controller=Controller(output,doc,worker);Set(controller,"bannerProgress",new CompactReviewProgress(doc));
            var blocker=Path.Combine(output,"save-blocker-"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"synthetic blocker");
            Set(controller,"pending",new PendingReviews(new ReviewStore(blocker)));
            try
            {
                Require(!controller.SaveBannerEditedText()&&controller.IsReviewUnsaved(doc.Id),"Failed protected storage was reported as saved.");
                Require(doc.OutputText=="Keep this unsaved correction."&&doc.InsertedAt==null&&controller.BannerReview==doc,
                    "A save failure lost the user's draft or inserted it.");
                checks.Add("Save text reports storage failure and preserves editable draft");
            }
            finally{File.Delete(blocker);}
        }
    }
    public static void Render(string theme,string output,List<string> checks)
    {
        CheckDetailsRetranscription(theme,output,checks);
        {
            var dismissed=Document();var owner=Controller(output,dismissed);
            Property(owner,"History",new List<HistoryEntry>());Set(owner,"bannerProgress",new CompactReviewProgress(dismissed));
            var view=new BannerWindow(owner);Set(owner,"banner",view);
            try
            {
                owner.SaveReview(dismissed);owner.Cancel();
                Require(!owner.ShowBanner&&owner.BannerReview==null&&owner.Pending.Any(d=>d.Id==dismissed.Id),
                    "The banner X deleted protected text or left the banner visible.");
                Require(new WindowInteropHelper(view).Handle==0,"Banner X created a native window in the windowless check.");
                checks.Add("banner X hides result and preserves protected transcript "+theme);
            }
            finally{view.Close();}
        }
        var doc=Document();doc.VerificationModel="second";doc.Verification="Prepared second transcript.";
        doc.VerificationCompleted=true;doc.Pieces=[new("0123456789abcdef",doc.Primary,doc.Verification,true)];doc.KeepRemaining();
        var controller=Controller(output,doc);Set(controller,"bannerProgress",new CompactReviewProgress(doc));
        var banner=new BannerWindow(controller);
        var lease=ClipboardLifetimeChecks.Lease();ClipboardRecovery.Hold(lease);
        try
        {
            typeof(BannerWindow).GetMethod("RefreshContent",Fields)!.Invoke(banner,null);
            var content=(FrameworkElement)banner.Content;banner.Content=null;Layout(content,392,88);
            var insertAction=Find<Button>(content).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Insert completed dictation");
            Require(insertAction.IsEnabled&&!insertAction.Focusable&&insertAction.Content?.ToString()=="Insert",
                "The idle banner replaced Insert with recovery or took keyboard focus.");
            Require(!Find<Button>(content).Any(b=>b.Content?.ToString()=="Resolve"),"A recovery action remains in the banner.");
            Require(new WindowInteropHelper(banner).Handle==0,"Pending banner render created a native window.");
            Save(content,392,88,Path.Combine(output,"pending-insert-banner-"+theme+".png"));
            checks.Add("pending banner keeps Insert without recovery dialog "+theme);
        }
        finally{ClipboardRecovery.TryRestorePending();banner.Close();}
        var review=new CompactReviewWindow(controller);
        try
        {
            var root=(FrameworkElement)review.Content;review.Content=null;
            Layout(root,560,720);
            var second=Find<TextBox>(root).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Complete second-model transcript");
            Require(second.IsReadOnly&&second.Text==doc.Verification&&second.ActualHeight>0,"The full second transcript is hidden or editable.");
            var insert=Find<Button>(root).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Insert");
            Require(!insert.Focusable&&insert.IsEnabled,"Review Insert can steal the chosen cursor or is unavailable.");
            Require(Find<Button>(root).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Transcribe again"&&b.IsEnabled),"Review still uses a separate second-transcript picker.");
            Require(Find<Button>(root).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Use this transcript"),"The complete alternative cannot be selected in one action.");
            Require(new WindowInteropHelper(review).Handle==0,"Review rendering created a native window.");
            Save(root,560,720,Path.Combine(output,"review-second-transcript-"+theme+".png"));checks.Add("selected-caret Review and full second transcript "+theme);
            var again=Find<Button>(root).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Transcribe again");
            again.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(root,400,600);
            Require(Find<RetranscriptionPicker>(root).Single().Visibility==Visibility.Visible&&Find<CompactReviewPanel>(root).Single().Visibility==Visibility.Collapsed,
                "Model selection did not replace content inside the same compact Review.");
            Require(new WindowInteropHelper(review).Handle==0,"Opening inline model selection created a native window.");
            Save(root,400,600,Path.Combine(output,"review-inline-model-compact-"+theme+".png"));
            again.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(root,560,720);
            Require(Find<CompactReviewPanel>(root).Single().Visibility==Visibility.Visible,"Closing model selection failed to restore Review content.");
            checks.Add("same Review inline model choice at compact width "+theme);
            var panel=Find<CompactReviewPanel>(root).Single();
            Property(controller,"History",new List<HistoryEntry>());
            typeof(CompactReviewPanel).GetMethod("BeginTextEdit",Fields)!.Invoke(panel,[(int?)0]);
            var editor=Find<TextBox>(root).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Edit final transcript");
            editor.Text="A correction before choosing the complete alternative.";
            Find<Button>(root).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Use this transcript").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(doc.OutputText==doc.Verification&&editor.Text==doc.Verification&&doc.ManualDraftArchive.Contains("A correction before choosing the complete alternative."),
                "Choosing the whole alternative left stale editor text or lost the previous correction.");
            Find<Button>(root).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Save text").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(doc.InsertedAt==null&&controller.BannerReview==doc&&doc.UnresolvedCount==0,"Whole-alternative Save performed insertion or dismissed the saved result.");
            Layout(root,560,720);Save(root,560,720,Path.Combine(output,"review-alternative-saved-"+theme+".png"));
            checks.Add("whole alternative updates active editor and saves without insertion "+theme);
        }
        finally{review.Close();}
        var picker=new RetranscriptionPicker(()=>controller.Models.Where(m=>m.Id=="second").ToList(),_=>"Synthetic request retained here.");
        picker.Toggle();Layout(picker,510,230);
        Require(picker.Visibility==Visibility.Visible&&Find<ComboBox>(picker).Single().Items.Count==1,"Inline picker lost its eligible models.");
        Find<Button>(picker).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Transcribe").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Find<TextBlock>(picker).Any(t=>t.Text=="Synthetic request retained here."),"Retranscription errors were not displayed in the same Review content.");
        Layout(picker,510,280);
        Save(picker,510,280,Path.Combine(output,"inline-transcription-picker-"+theme+".png"));checks.Add("inline model picker and in-place error "+theme);
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
    static void CheckDetailsRetranscription(string theme,string output,List<string> checks)
    {
        foreach(var outcome in new[]{"completed","cancelled","failed"})
        {
            var source=Document();source.ManualFinalDraft="Preserved correction.";
            source.AttachProvenance(new("source-session",new("first","first-revision"),null,DateTimeOffset.UtcNow));
            var owner=Controller(output,source);
            var banner=new BannerWindow(owner);Set(owner,"banner",banner);
            var details=new ReviewWindow(source,owner.SaveReview,_=>{},_=>Task.FromResult(new ReviewInsertionResult(false,"Unused")),_=>{});
            Set(owner,"reviewWindow",details);details.SetTranscriptionBusy(true);
            var child=SavedVerification.Begin(source,"second");
            if(outcome=="completed")SavedVerification.Complete(child,source,new(source.Primary,"Alternative complete wording.",
                [new("0123456789abcdef",source.Primary,"Alternative complete wording.",true)],"first","first-revision","second","second-revision","new-comparison","en","forced",4));
            else child.Reason="Second transcript "+outcome+". Original text is preserved.";
            Property(owner,"History",new List<HistoryEntry>{new(source,4),new(child,4)});
            var closing=false;
            System.ComponentModel.CancelEventHandler refuseClose=(_,e)=>
            {
                e.Cancel=true;closing=true;
                throw new InvalidOperationException("Saved retranscription closed Details instead of updating the same Review window.");
            };
            details.Closing+=refuseClose;
            try
            {
                try{typeof(AppController).GetMethod("ShowBannerReview",Fields)!.Invoke(owner,[child,true]);}
                catch(TargetInvocationException error) when(error.InnerException!=null)
                {throw new InvalidOperationException(error.InnerException.Message,error.InnerException);}
                Require(!closing&&ReferenceEquals(typeof(AppController).GetField("reviewWindow",Fields)!.GetValue(owner),details)&&details.ReviewId==child.Id,
                    "Saved retranscription replaced Details or kept its source binding.");
                Require(((UIElement)details.Content).IsEnabled,"Details remained disabled after saved transcription "+outcome+".");
                Require(typeof(AppController).GetField("compactReviewWindow",Fields)!.GetValue(owner)==null,
                    "Details retranscription opened an extra compact Review.");
                var root=(FrameworkElement)details.Content;
                Require(LogicalFind<TextBox>(root).Any(t=>t.Text=="Preserved correction."),"Rebinding Details lost the authoritative edited draft.");
                var originals=LogicalFind<TabControl>(root).Single().Items.OfType<TabItem>().ToList();
                Require(originals.Count==3,"Rebound Details did not rebuild the alternative source tab.");
                Require(LogicalFind<TextBox>((DependencyObject)originals[2].Content).Any(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Original transcript from second"&&
                    t.Text==(outcome=="completed"?"Alternative complete wording.":"")),"Details retained stale original transcripts after rebinding.");
                Require(new WindowInteropHelper(details).Handle==0&&new WindowInteropHelper(banner).Handle==0,
                    "Details retranscription checks created a native window.");
                Require(source.ManualFinalDraft=="Preserved correction."&&source.VerificationModel==null&&source.Provenance!.SessionId=="source-session",
                    "Rebinding modified the source Review or its provenance.");
                checks.Add("same Details window after saved transcription "+outcome+" "+theme);
            }
            finally{details.Closing-=refuseClose;details.Close();banner.Close();}
        }
        CheckDetailsReplayRebinding(theme,output,checks);
        CheckEmptyDetailsReplay(theme,output,checks);
        CheckCompactSavedReplay(theme,output,checks);
    }
    static void CheckEmptyDetailsReplay(string theme,string output,List<string> checks)
    {
        foreach(var outcome in new[]{"empty","cancelled","failed"})
        {
            var source=Document();source.ManualFinalDraft="Restore this saved correction.";
            var owner=Controller(output,source);var banner=new BannerWindow(owner);Set(owner,"banner",banner);
            var empty=new ReviewDocument{PrimaryModel="first",RequiresExplicitInsert=true,RetranscriptionOf=source.Id};
            var details=new ReviewWindow(source,owner.SaveReview,_=>{},_=>Task.FromResult(new ReviewInsertionResult(false,"Unused")),_=>{});
            details.Rebind(empty);details.SetTranscriptionBusy(true);Set(owner,"reviewWindow",details);
            var runType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
            var run=RuntimeHelpers.GetUninitializedObject(runType);
            var gate=new CompletionDestinationGate();gate.BeginFinish();gate.ConfirmOriginalField(false);
            Set(run,"Review",empty);Set(run,"Text","");Set(run,"Settings",new Settings{AfterRecording=AfterRecordingAction.PreviewFirst});
            Set(run,"CompletionGate",gate);Set(run,"FinishDestinationCheck",Task.CompletedTask);Set(run,"NormalCompletion",outcome=="empty");
            runType.GetField("ReviewFallback",Fields)?.SetValue(run,
                System.Text.Json.JsonSerializer.Deserialize<ReviewDocument>(System.Text.Json.JsonSerializer.Serialize(source))!);
            Set(owner,"active",run);
            try
            {
                ((Task)typeof(AppController).GetMethod("CompleteRun",Fields)!.Invoke(owner,[run])!).GetAwaiter().GetResult();
                Require(((UIElement)details.Content).IsEnabled&&!details.IsTranscribing,
                    "Zero-text "+outcome+" replay left Details disabled after CompleteRun.");
                Require(ReferenceEquals(details.Document,source)&&details.Document.OutputText=="Restore this saved correction.",
                    "Zero-text replay did not restore the previous valid Review and edits.");
                Require(empty.Primary==""&&empty.RetranscriptionOf==source.Id&&empty.InsertedAt==null,
                    "Recovery rewrote the empty attempt as a successful transcript.");
                Require(typeof(AppController).GetField("compactReviewWindow",Fields)!.GetValue(owner)==null&&
                    new WindowInteropHelper(details).Handle==0&&new WindowInteropHelper(banner).Handle==0,
                    "Zero-text Details recovery created another/native window.");
                Require(ReferenceEquals(details.Document,source),"Replay recovery rebound a detached snapshot instead of the current saved Review.");
                Property(owner,"History",new List<HistoryEntry>());
                var editor=(TextBox)typeof(ReviewWindow).GetField("manual",Fields)!.GetValue(details)!;
                editor.Text="Edited after replay recovery.";
                Require(source.OutputText=="Edited after replay recovery."&&owner.Pending.Any(d=>d.Id==source.Id&&d.OutputText==source.OutputText),
                    "Edits after empty replay recovery were reported saved but lost to a stale snapshot.");
                checks.Add("zero-text "+outcome+" replay restores the same editable Details "+theme);
            }
            finally{details.Close();banner.Close();}
        }
    }
    static void CheckCompactSavedReplay(string theme,string output,List<string> checks)
    {
        var source=Document();source.RequiresExplicitInsert=true;source.ManualFinalDraft="Keep the compact correction.";
        var owner=Controller(output,source);Set(owner,"bannerProgress",new CompactReviewProgress(source));
        var compact=new CompactReviewWindow(owner);Set(owner,"compactReviewWindow",compact);
        var closed=false;compact.Closed+=(_,_)=>closed=true;
        var runType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
        var prepare=typeof(AppController).GetMethod("PrepareRecordingReview",Fields)!;
        var current=source;
        object? lastRun=null;
        try
        {
            for(var pass=0;pass<3;pass++)
            {
                var next=new ReviewDocument{RequiresExplicitInsert=true,PrimaryModel="first",RetranscriptionOf=current.Id};
                typeof(AppController).GetMethod("BindTranscribingReview",Fields)!.Invoke(owner,[current,next]);
                var run=RuntimeHelpers.GetUninitializedObject(runType);Set(run,"Review",next);Set(run,"ReviewFallback",source);
                prepare.Invoke(owner,[run,true]);compact.Refresh();
                Require(!closed&&ReferenceEquals(owner.BannerReview,next)&&ReferenceEquals(typeof(AppController).GetField("compactReviewWindow",Fields)!.GetValue(owner),compact),
                    "A current saved-recording language replay replaced its existing compact Review.");
                current=next;lastRun=run;
            }
            Require((bool)typeof(AppController).GetMethod("RestoreEmptySavedReview",Fields)!.Invoke(owner,[lastRun])!&&ReferenceEquals(owner.BannerReview,source),
                "Empty compact replay did not restore the previous saved text.");
            compact.Refresh();
            Require(!closed&&source.OutputText=="Keep the compact correction."&&current.Primary==""&&current.InsertedAt==null,
                "Compact recovery discarded saved edits or rewrote the empty attempt.");
            Require(new WindowInteropHelper(compact).Handle==0,"Compact replay checks created a native window.");
            prepare.Invoke(owner,[lastRun,false]);
            Require(closed&&owner.BannerReview==null,"An unrelated new recording incorrectly retained the saved Review surface.");
            checks.Add("saved compact replay lineage preserves its window and restores empty results "+theme);
        }
        finally{if(!closed)compact.Close();}
    }
    static void CheckDetailsReplayRebinding(string theme,string output,List<string> checks)
    {
        var source=Document();source.ManualFinalDraft="Unsaved source correction.";source.InsertedAt=DateTimeOffset.UtcNow;
        var sourceReceipt=source.InsertedAt;
        var owner=Controller(output,source);var banner=new BannerWindow(owner);Set(owner,"banner",banner);
        string? destinationId=null;
        var details=new ReviewWindow(source,owner.SaveReview,_=>{},_=>Task.FromResult(new ReviewInsertionResult(false,"Unused")),doc=>destinationId=doc.Id);
        Set(owner,"reviewWindow",details);
        var current=source;
        var bind=typeof(AppController).GetMethod("BindTranscribingReview",Fields)!;
        try
        {
            // Each correction creates another Review; UI handover itself must not
            // start Record, enumerate devices, connect to the API or create a window.
            for(var pass=0;pass<3;pass++)
            {
                var next=SavedVerification.Begin(current,"second");
                bind.Invoke(owner,[current,next]);current=next;
                Require(details.ReviewId==next.Id&&details.IsTranscribing&&!((UIElement)details.Content).IsEnabled,
                    "Successive language replay left Details bound to an earlier comparison.");
            }
            SavedVerification.Complete(current,source,new(source.Primary,"Complete replay alternative.",
                [new("0123456789abcdef",source.Primary,"Complete replay alternative.",true)],"first",null,"second","latest-second-revision","last-comparison","en","forced",4));
            Property(owner,"History",new List<HistoryEntry>{new(current,4)});
            typeof(AppController).GetMethod("ShowBannerReview",Fields)!.Invoke(owner,[current,true]);
            Require(details.ReviewId==current.Id&&!details.IsTranscribing&&((UIElement)details.Content).IsEnabled,
                "The final replay did not restore the same editable Details window.");
            Require(LogicalFind<TextBlock>((DependencyObject)details.Content).Any(t=>t.Text.Contains("latest-secon")),
                "Details retained the previous model revision after replay.");
            Require(LogicalFind<Button>((DependencyObject)details.Content).Single(b=>b.Content?.ToString()=="Transcribe again").IsEnabled,
                "The rebound saved comparison cannot be transcribed again.");
            var tabs=LogicalFind<TabControl>((DependencyObject)details.Content).Single().Items.OfType<TabItem>().ToList();
            Property(owner,"History",new List<HistoryEntry>());
            LogicalFind<Button>((DependencyObject)tabs[2].Content).Single(b=>b.Content?.ToString()=="Use this transcript").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(current.OutputText=="Complete replay alternative."&&owner.Pending.Any(d=>d.Id==current.Id&&d.OutputText=="Complete replay alternative."),
                "Rebound whole-transcript/save callbacks modified the earlier Review.");
            LogicalFind<Button>((DependencyObject)details.Content).Single(b=>b.Content?.ToString()=="Choose destination").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(destinationId==current.Id&&current.InsertedAt==null&&source.InsertedAt==sourceReceipt&&source.ManualFinalDraft=="Unsaved source correction.",
                "Rebound actions targeted the source or changed its edits/insertion receipt.");
            Require(new WindowInteropHelper(details).Handle==0&&new WindowInteropHelper(banner).Handle==0,
                "Successive replay checks created a native window.");
            checks.Add("successive saved language replay rebinds Details and current-document actions "+theme);
        }
        finally{details.Close();banner.Close();}
    }
    static void Layout(FrameworkElement root,int width,int height){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
    static void Save(FrameworkElement root,int width,int height,string path)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        // A detached Window content tree loses the native Window's Canvas brush.
        var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle((Brush)Application.Current.FindResource("Canvas"),null,new Rect(0,0,width,height));
        bitmap.Render(background);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);png.Save(file);
    }
}
