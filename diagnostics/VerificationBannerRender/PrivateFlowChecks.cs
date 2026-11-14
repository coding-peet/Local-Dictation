using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.Core;
public static class PrivateFlowChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object x,string name,object? value)=>x.GetType().GetField(name,Fields)!.SetValue(x,value);
    static T Get<T>(object x,string name)=>(T)x.GetType().GetField(name,Fields)!.GetValue(x)!;
    static void Require(bool result,string why){if(!result)throw new InvalidOperationException(why);}
    static IEnumerable<T> Find<T>(DependencyObject p)where T:DependencyObject{if(p is T found)yield return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(p);i++)foreach(var c in Find<T>(VisualTreeHelper.GetChild(p,i)))yield return c;}
    public static void Run(string output,List<string> checks)
    {
        var path=Path.Combine(output,"private-flow",Guid.NewGuid().ToString("N"));var d=new ReviewDocument();d.SetPrimary("private synthetic recording");
        var owner=ReviewWorkflowChecks.Controller(output,d);var pending=new PendingReviews(new ReviewStore(Path.Combine(path,"review")));
        Set(owner,"pending",pending);Set(owner,"historyStore",new HistoryStore(Path.Combine(path,"history")));Set(owner,"<History>k__BackingField",new List<HistoryEntry>());
        Require(owner.SetPrivateNextRecording(true),"Private-next cannot be armed while idle.");
        var runType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;var run=RuntimeHelpers.GetUninitializedObject(runType);runType.GetField("Review")!.SetValue(run,d);Set(owner,"active",run);
        var claim=typeof(AppController).GetMethod("ClaimPrivateRecording",Fields);Require(claim!=null,"Private policy is not claimed by an owned recording operation.");claim!.Invoke(owner,[run]);
        Require(d.IsPrivate&&!owner.PrivateNextRecording,"Owned session did not snapshot and consume private-next exactly once.");Set(owner,"active",null);
        owner.SaveReview(d);Require(owner.PrivateReviewCount==1&&pending.Documents.Count==0&&!Directory.Exists(path),"Private text reached protected History/Pending storage.");
        var cloned=JsonSerializer.Deserialize<ReviewDocument>(JsonSerializer.Serialize(d))!;owner.SaveReview(cloned);
        Require(!Directory.Exists(path),"A copied private Review became persistent.");
        d.ManualFinalDraft="edited private words";Set(owner,"bannerProgress",new CompactReviewProgress(d));
        var panel=new CompactReviewPanel(owner);panel.Refresh();panel.Measure(new(520,700));panel.Arrange(new Rect(0,0,520,700));panel.UpdateLayout();
        Require(!Find<TextBlock>(panel).Any(t=>t.Text is "Saved" or "Draft saved"),"Private editor claims the transcript is saved.");
        Require(Find<TextBlock>(panel).Any(t=>t.Text.Contains("memory",StringComparison.OrdinalIgnoreCase)),"Private editor lacks its memory-only limit.");
        Require(PresentationSource.FromVisual(panel)==null&&!Directory.Exists(path),"Private check opened a native window or wrote private data.");
        owner.DismissReview(d.Id);Require(owner.PrivateReviewCount==0&&pending.Documents.Count==0&&!Directory.Exists(path),"Explicit private dismiss persists or retains the transcript.");
        checks.Add("Private owned-session snapshot, clone, controller save/dismiss and honest editor state without persistence or native input");
    }
}
