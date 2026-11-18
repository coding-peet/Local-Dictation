using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

public static class HistoryInsertChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    sealed class Delivery(bool accepted=true,bool uncertain=false):ITextDelivery
    {
        public nint Window=>42;public bool IsConflict{get;private set;}public bool HasTransportFailure=>uncertain;
        public string Reason=>accepted?"Inserted synthetic copy.":"Synthetic insertion refused.";public string InsertedText{get;private set;}="";
        public Action? OnApply;public int Calls;public bool IsOriginalFieldCurrent(CancellationToken token)=>!IsConflict;
        public void Invalidate()=>IsConflict=true;public void Suspend(){}public bool OwnsProgrammaticFocus(nint window)=>false;
        public bool Apply(string text,CancellationToken token,bool restoreFocus=false){token.ThrowIfCancellationRequested();Calls++;OnApply?.Invoke();token.ThrowIfCancellationRequested();if(accepted&&!IsConflict)InsertedText=text;return accepted&&!IsConflict;}
        public bool Apply(string text,CancellationToken token,DeliveryIntent intent)=>Apply(text,token,intent.RestoreFocus);
    }
    static ReviewDocument Source()
    {
        var source=new ReviewDocument{PrimaryModel="first",Language="en"};source.SetPrimary("Alpha Beta Gamma");
        source.InsertedAt=DateTimeOffset.Parse("2026-11-17T10:00:00Z");return source;
    }
    static AppController Controller(string output,ReviewDocument source,AutomationWorker worker,Delivery delivery)
    {
        var owner=ReviewWorkflowChecks.Controller(output,source,worker);
        Set(owner,"<History>k__BackingField",new List<HistoryEntry>{new(source,0){HasAudio=false}});
        Set(owner,"historyInsertionDestination",(Func<HistoryInsertionDestination?>)(()=>new(42,77)));
        Set(owner,"restoreHistoryDestination",(Action<HistoryInsertionDestination,CancellationToken>)((_,token)=>token.ThrowIfCancellationRequested()));
        Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>delivery));
        return owner;
    }
    static Task<ReviewInsertionResult> Insert(AppController owner,ReviewDocument source,string text)
    {
        var method=typeof(AppController).GetMethod("InsertHistoryAtCursor",Fields,null,[typeof(string),typeof(string)],null);
        Require(method!=null,"Direct History Insert controller action is missing.");
        return (Task<ReviewInsertionResult>)method!.Invoke(owner,[source.Id,text])!;
    }
    public static void Run(string output,List<string> checks)
    {
        Require(typeof(AppController).GetMethod("InsertHistoryAtCursor",Fields)!=null,"Direct History Insert controller action is missing.");
        var context=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(null);
        try{CheckAsync(output,checks).GetAwaiter().GetResult();}finally{SynchronizationContext.SetSynchronizationContext(context);}
        var source=Source();using var worker=new AutomationWorker();var controller=Controller(output,source,worker,new Delivery());
        using var panel=new HistoryPanel(controller);panel.SetActive(true);
        var transcript=Get<TextBox>(panel,"transcript");var menu=Get<ContextMenu>(panel,"moreMenu");
        var snapshot=typeof(HistoryPanel).GetMethod("SnapshotHistoryInsertionText",Fields);
        var insert=Get<object>(panel,"insertAtCursor");
        Require(snapshot!=null&&insert is Button button&&(string)button.Content=="Insert at cursor"&&
            LogicalTreeHelper.GetParent(button) is WrapPanel row&&ReferenceEquals(row.Children[0],button)&&
            !menu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="Insert at cursor"),
            "History Insert must be the first visible action button, not hidden in More.");
        transcript.Select(6,4);Require((string?)snapshot!.Invoke(panel,null)=="Beta",$"History did not snapshot the highlighted portion: visible={transcript.Text}, selection={transcript.SelectedText}, snapshot={snapshot.Invoke(panel,null)}, entries={Get<ListBox>(panel,"items").Items.Count}.");
        transcript.Select(0,0);Require((string?)snapshot.Invoke(panel,null)=="Alpha Beta Gamma","History did not fall back to the entire displayed transcript.");
        checks.Add("History direct Insert uses highlighted text or full transcript without a placement window");
    }
    static async Task CheckAsync(string output,List<string> checks)
    {
        foreach(var text in new[]{"Beta","Alpha Beta Gamma"})
        {
            using var worker=new AutomationWorker();var source=Source();var original=JsonSerializer.Serialize(source);var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);var restored=false;
            Set(owner,"restoreHistoryDestination",(Action<HistoryInsertionDestination,CancellationToken>)((_,_)=>restored=true));
            Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{Require(restored,"History captured before returning to the external window.");return delivery;}));
            var result=await Insert(owner,source,text);
            Require(result.Success&&delivery.Calls==1&&delivery.InsertedText==text,"History failed to insert the exact independent copy.");
            Require(JsonSerializer.Serialize(source)==original&&owner.History.Count==1&&owner.PrivateReviewCount==0&&owner.Pending.Count==0,"History insertion changed its saved source or created a saved/private duplicate.");
            var nextDelivery=new Delivery();Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>nextDelivery));
            Require((await Insert(owner,source,text)).Success&&delivery.Calls==1&&nextDelivery.Calls==1,"A second explicit reuse did not freshly capture the destination.");
            checks.Add("direct History copy preserves inserted source: "+(text=="Beta"?"selection":"whole text"));
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);
            Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>
            {
                Get<GlobalInput>(owner,"input").Epoch.UserInput(42,0);
                typeof(AppController).GetMethod("DestinationChanged",Fields)!.Invoke(owner,[new DestinationInput("Queued focus restoration",true,42)]);
                return delivery;
            }));
            Require((await Insert(owner,source,"Beta")).Success&&delivery.Calls==1,"Queued expected focus notification cancelled direct History insertion.");
            checks.Add("expected restoration focus events do not invalidate the explicit History command");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);var captures=0;var changes=0;
            owner.Changed+=()=>{if(++changes==2)typeof(AppController).GetMethod("DestinationChanged",Fields)!.Invoke(owner,[new DestinationInput("Cancelled before capture worker",false,99)]);};
            Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{captures++;return delivery;}));
            Require(!(await Insert(owner,source,"Beta")).Success&&captures==0&&delivery.Calls==0,"Cancelled History command still read a target in the capture worker.");
            checks.Add("History cancellation is checked inside the capture worker before reading a field");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);nint foreground=42;
            Set(owner,"insertionForegroundWindow",(Func<nint>)(()=>foreground));
            Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{foreground=99;typeof(AppController).GetMethod("DestinationChanged",Fields)!.Invoke(owner,[new DestinationInput("Unexpected foreground",true,99)]);return delivery;}));
            Require(!(await Insert(owner,source,"Beta")).Success&&delivery.Calls==0,"Unexpected focus-only foreground reached History dispatch.");
            checks.Add("unexpected foreground focus stops History dispatch despite restored-focus epoch bypass");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);
            delivery.OnApply=()=>typeof(AppController).GetMethod("DestinationChanged",Fields)!.Invoke(owner,[new DestinationInput("Input at Apply entry",false,42)]);
            Require(!(await Insert(owner,source,"Beta")).Success&&delivery.InsertedText=="","History cancellation did not reach the delivery token.");
            checks.Add("History command cancellation remains linked through delivery");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);var restores=0;
            Set(owner,"historyInsertionDestination",(Func<HistoryInsertionDestination?>)(()=>null));
            Set(owner,"restoreHistoryDestination",(Action<HistoryInsertionDestination,CancellationToken>)((_,_)=>restores++));
            Require(!(await Insert(owner,source,"Beta")).Success&&restores==0&&delivery.Calls==0,"Missing History target reached activation or delivery.");
            checks.Add("missing History destination refuses without dispatch");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);var captures=0;
            Set(owner,"restoreHistoryDestination",(Action<HistoryInsertionDestination,CancellationToken>)((_,_)=>throw new InvalidOperationException("Closed synthetic target")));
            Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{captures++;return delivery;}));
            Require(!(await Insert(owner,source,"Beta")).Success&&captures==0&&delivery.Calls==0&&owner.Pending.Count==0,"Failed target restoration reached capture/paste or saved a duplicate.");
            checks.Add("closed History destination keeps source and skips capture");
        }
        using(var worker=new AutomationWorker())using(var release=new ManualResetEventSlim())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Set(owner,"restoreHistoryDestination",(Action<HistoryInsertionDestination,CancellationToken>)((_,token)=>{entered.TrySetResult();release.Wait(token);}));
            var first=Insert(owner,source,"Beta");await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Require(owner.IsApplyingReview,"History activation did not reserve the insertion operation.");
            var second=await Insert(owner,source,"Beta");owner.Start();Require(Get<object?>(owner,"active")==null,"Recording started during History insertion.");
            release.Set();Require((await first).Success&&!second.Success&&delivery.Calls==1,"Concurrent History clicks duplicated delivery.");
            checks.Add("History reserves before activation and blocks recording/concurrent insert");
        }
        using(var worker=new AutomationWorker())using(var release=new ManualResetEventSlim())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Set(owner,"restoreHistoryDestination",(Action<HistoryInsertionDestination,CancellationToken>)((_,token)=>{entered.TrySetResult();release.Wait(token);}));
            var first=Insert(owner,source,"Beta");await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var other=new ReviewDocument{PrimaryModel="first"};other.SetPrimary("Other pending Review.");
            var otherInsert=owner.InsertReviewAtSelectedCursor(other);var reserved=otherInsert.IsCompleted&&!(await otherInsert).Success;
            release.Set();await first;await otherInsert;
            Require(reserved,"Direct Review insertion bypassed History's activation reservation.");
            checks.Add("History activation also reserves other direct Review insertion paths");
        }
        using(var worker=new AutomationWorker())using(var release=new ManualResetEventSlim())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var captures=0;
            Set(owner,"restoreHistoryDestination",(Action<HistoryInsertionDestination,CancellationToken>)((_,token)=>{entered.TrySetResult();release.Wait(token);}));
            Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>{captures++;return delivery;}));
            var inserting=Insert(owner,source,"Beta");await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            typeof(AppController).GetMethod("DestinationChanged",Fields)!.Invoke(owner,[new DestinationInput("Synthetic user input",false,42)]);release.Set();
            Require(!(await inserting).Success&&captures==0&&delivery.Calls==0,"Intervening user input allowed a stale History insertion.");
            checks.Add("intervening user input cancels History activation before capture");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var delivery=new Delivery();var owner=Controller(output,source,worker,delivery);var restores=0;
            var deferred=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var method=typeof(AppController).GetMethod("InsertHistoryAtCursorAfterMenuClosed",Fields);
            Require(method!=null,"History command is not reserved before menu-close defer.");
            Set(owner,"restoreHistoryDestination",(Action<HistoryInsertionDestination,CancellationToken>)((_,_)=>restores++));
            var inserting=(Task<ReviewInsertionResult>)method!.Invoke(owner,[source.Id,"Beta",(Func<Task>)(()=>deferred.Task)])!;
            typeof(AppController).GetMethod("DestinationChanged",Fields)!.Invoke(owner,[new DestinationInput("Input during menu close",false,99)]);
            deferred.SetResult();Require(!(await inserting).Success&&restores==0&&delivery.Calls==0,"Input during menu closure reached another destination.");
            checks.Add("History snapshots and reserves before deferred menu closure");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var original=JsonSerializer.Serialize(source);var failed=new Delivery(false);var owner=Controller(output,source,worker,failed);
            var receiptQueue=new ConfirmedReceiptQueue<PendingPasteConfirmation>();
            var native=(WindowsTextDelivery)RuntimeHelpers.GetUninitializedObject(typeof(WindowsTextDelivery));
            Set(native,"window",(nint)42);Set(native,"lifecycleGate",new object());Set(native,"confirmedReceipts",receiptQueue);
            failed.OnApply=()=>{var id=Get<Dictionary<string,ReviewDocument>>(owner,"historyInsertCopies").Keys.Single();Get<Dictionary<string,ITextDelivery>>(owner,"destinations")[id]=native;};
            Require(!(await Insert(owner,source,"Beta")).Success,"Synthetic failure was marked inserted.");
            Require(Get<Dictionary<string,ITextDelivery>>(owner,"destinations").Values.Contains(native),"History discarded a receipt carrier merely because HasTransportFailure was false.");
            receiptQueue.Publish(new("Beta",true));typeof(AppController).GetMethod("DrainConfirmedPastes",Fields)!.Invoke(owner,null);
            Require((await Insert(owner,source,"Beta")).Success&&failed.Calls==1&&receiptQueue.Current==null&&JsonSerializer.Serialize(source)==original&&owner.Pending.Count==0,
                "A late History receipt was lost, duplicated, or saved into the source.");
            checks.Add("late native History receipt survives false transport flag and prevents duplicate retry");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var failed=new Delivery(false);var owner=Controller(output,source,worker,failed);
            var native=(WindowsTextDelivery)RuntimeHelpers.GetUninitializedObject(typeof(WindowsTextDelivery));
            Set(native,"window",(nint)42);Set(native,"lifecycleGate",new object());Set(native,"confirmedReceipts",new ConfirmedReceiptQueue<PendingPasteConfirmation>());
            var pendingType=typeof(WindowsTextDelivery).GetNestedType("PendingNativePaste",BindingFlags.NonPublic)!;
            var pending=RuntimeHelpers.GetUninitializedObject(pendingType);Set(pending,"<DispatchedAt>k__BackingField",System.Diagnostics.Stopwatch.GetTimestamp());Set(native,"pendingPaste",pending);
            failed.OnApply=()=>{var id=Get<Dictionary<string,ReviewDocument>>(owner,"historyInsertCopies").Keys.Single();Get<Dictionary<string,ITextDelivery>>(owner,"destinations")[id]=native;};
            Require(!(await Insert(owner,source,"Beta")).Success&&native.HasPendingAcknowledgement,"Synthetic pending acknowledgement was not retained.");
            Require(!(await Insert(owner,source,"Beta")).Success&&failed.Calls==1,"An outstanding acknowledgement permitted replay.");
            Set(owner,"historyInsertionExpiresAt",DateTimeOffset.MinValue);typeof(AppController).GetMethod("PruneHistoryInsertionAttempt",Fields)!.Invoke(owner,null);
            Require(Get<Dictionary<string,ReviewDocument>>(owner,"historyInsertCopies").Count==1,"Expiry removed a pending native acknowledgement.");
            Set(native,"pendingPaste",null);typeof(AppController).GetMethod("PruneHistoryInsertionAttempt",Fields)!.Invoke(owner,null);
            Require(Get<Dictionary<string,ReviewDocument>>(owner,"historyInsertCopies").Count==0&&Get<Dictionary<string,ITextDelivery>>(owner,"destinations").Count==0,"Terminal abandoned History copy was not pruned.");
            checks.Add("pending native acknowledgement blocks replay and expiry, terminal copies expire");
        }
        using(var worker=new AutomationWorker())using(var release=new ManualResetEventSlim())
        {
            var source=Source();var original=JsonSerializer.Serialize(source);var failed=new Delivery(false);var owner=Controller(output,source,worker,failed);
            var receiptQueue=new ConfirmedReceiptQueue<PendingPasteConfirmation>();var native=(WindowsTextDelivery)RuntimeHelpers.GetUninitializedObject(typeof(WindowsTextDelivery));
            Set(native,"window",(nint)42);Set(native,"lifecycleGate",new object());Set(native,"confirmedReceipts",receiptQueue);
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            failed.OnApply=()=>{var id=Get<Dictionary<string,ReviewDocument>>(owner,"historyInsertCopies").Keys.Single();Get<Dictionary<string,ITextDelivery>>(owner,"destinations")[id]=native;entered.TrySetResult();release.Wait();receiptQueue.Publish(new("Beta",true));};
            try
            {
                var insertion=Insert(owner,source,"Beta");await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Require(!(await insertion.WaitAsync(TimeSpan.FromSeconds(8))).Success,"A timed-out provider was reported as confirmed.");
                var unfinished=worker.UnfinishedWork;
                Require(unfinished!=null&&owner.IsApplyingReview,"Caller timeout released the active native-work reservation.");
                release.Set();try{await unfinished!.WaitAsync(TimeSpan.FromSeconds(2));}catch(OperationCanceledException){}
                typeof(AppController).GetMethod("DrainConfirmedPastes",Fields)!.Invoke(owner,null);
                Require((await Insert(owner,source,"Beta")).Success&&failed.Calls==1&&receiptQueue.Current==null&&JsonSerializer.Serialize(source)==original,"Receipt after provider timeout was lost or duplicated.");
                checks.Add("provider timeout retains work reservation and late receipt without replay");
            }
            finally{release.Set();}
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var failed=new Delivery(false,true);var owner=Controller(output,source,worker,failed);var accepted=new Delivery();var captures=0;
            Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>++captures==1?failed:accepted));
            Require(!(await Insert(owner,source,"Beta")).Success,"Synthetic uncertain insertion unexpectedly succeeded.");
            Require((await Insert(owner,source,"Gamma")).Success&&failed.IsConflict&&accepted.InsertedText=="Gamma"&&Get<Dictionary<string,ReviewDocument>>(owner,"historyInsertCopies").Count==0,
                "Changing History text leaked an abandoned carrier or reused the wrong copy.");
            checks.Add("new explicit History text retires terminal prior copy without memory accumulation");
        }
        using(var worker=new AutomationWorker())
        {
            var source=Source();var original=JsonSerializer.Serialize(source);var uncertain=new Delivery(false,true);var owner=Controller(output,source,worker,uncertain);var retry=new Delivery();var count=0;
            Set(owner,"captureInsertionTarget",(Func<CancellationToken,ITextDelivery>)(_=>++count==1?uncertain:retry));
            Require(!(await Insert(owner,source,"Beta")).Success,"Unconfirmed History paste was marked successful.");
            Require((await Insert(owner,source,"Beta")).Success&&uncertain.Calls==1&&retry.Calls==1&&JsonSerializer.Serialize(source)==original,"Explicit History retry replayed or modified the source.");
            checks.Add("explicit History retry preserves source and does not replay transport automatically");
        }
    }
}
