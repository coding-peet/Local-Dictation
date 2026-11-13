using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class HistoryIntegrationChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Property(object target,string name,object? value)=>Set(target,"<"+name+">k__BackingField",value);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject
    {if(parent is T found)yield return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(parent,i)))yield return child;}
    public static void Run(string theme,string output,List<string> checks)
    {
        var controller=(AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
        var directory=Path.Combine(output,"synthetic-history",Guid.NewGuid().ToString("N"));
        var store=new HistoryStore(directory);
        var first=new ReviewDocument{PrimaryModel="example/local-model",Language="en",RequestedLanguage="en"};
        first.SetPrimary(string.Concat(Enumerable.Repeat("Synthetic review notes. The selected recording stays in the main History page. ",120)));
        var second=new ReviewDocument{PrimaryModel="example/local-model",Created=first.Created.AddMinutes(-1)};
        second.SetPrimary("An earlier synthetic dictation.");
        var pcm=new byte[32000*4];for(var i=0;i<pcm.Length/2;i++)BitConverter.GetBytes((short)(Math.Sin(i*.06)*15000)).CopyTo(pcm,i*2);
        store.Add(first,pcm);store.Add(second,pcm);
        for(var i=2;i<20;i++)
        {
            var old=new ReviewDocument{PrimaryModel="example/local-model",Created=first.Created.AddMinutes(-i)};
            old.SetPrimary("Earlier synthetic recording with a wrapped label for a complete twenty-item history.");store.Add(old,pcm);
        }
        Set(controller,"historyStore",store);Property(controller,"History",store.Load());
        var pending=new PendingReviews(new ReviewStore(Path.Combine(directory,"pending")));Set(controller,"pending",pending);
        // Only invoke the real page factory: never construct SettingsWindow, enumerate
        // microphones, install hooks, create HWNDs, or touch the actual user's history.
        var settings=(SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));Set(settings,"controller",controller);
        var page=(FrameworkElement)typeof(SettingsWindow).GetMethod("HistoryPage",Fields)!.Invoke(settings,null)!;
        try
        {
            Layout(page,760,560);
            var list=Find<ListBox>(page).SingleOrDefault(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="Recent dictations");
            Require(list!=null,"Main History page still opens a popup instead of containing the dictations list.");
            var transcript=Find<TextBox>(page).Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="Selected dictation transcript");
            var player=Find<HistoryAudioPlayerControl>(page).Single();
            var playback=(HistoryAudioPlayback)typeof(HistoryAudioPlayerControl).GetField("playback",Fields)!.GetValue(player)!;
            Require(!playback.HasAudio,"Hidden History eagerly decrypted a recording.");
            void Activate(bool active)=>page.GetType().GetMethod("SetActive")!.Invoke(page,[active]);
            Activate(true);Layout(page,760,560);
            Require(list!.Items.Count==20&&transcript.Text==first.OutputText&&playback.HasAudio,"Embedded History lost selection, text or original audio.");
            Require(!Find<Button>(page).Any(b=>b.Content?.ToString()=="Open transcript and audio history"),"The embedded page still has the popup launcher.");
            var playerBounds=player.TransformToAncestor(page).TransformBounds(new Rect(0,0,player.ActualWidth,player.ActualHeight));
            Require(playerBounds.Bottom<=page.ActualHeight,"Full History or a long transcript pushes playback below the wide viewport.");
            checks.Add("full History keeps wide playback visible "+theme);
            Save(page,760,560,Path.Combine(output,"integrated-history-wide-"+theme+".png"));
            checks.Add("integrated main History content "+theme);
            var selected=list.SelectedItem;
            Property(controller,"History",store.Load());
            ((Action?)typeof(AppController).GetField("Changed",Fields)!.GetValue(controller))?.Invoke();
            Require((list.SelectedItem as DictationItem)?.Id==(selected as DictationItem)?.Id,"History refresh lost the selected dictation.");
            pending.Save(first);store.Delete(first.Id);Property(controller,"History",store.Load());
            ((Action?)typeof(AppController).GetField("Changed",Fields)!.GetValue(controller))?.Invoke();
            Require((list.SelectedItem as DictationItem) is {HasAudio:false,NeedsReview:true},"Evicted audio hid its unfinished text.");
            Require(!playback.HasAudio&&player.Visibility==Visibility.Collapsed,"Text-only recovery retained stale playback audio.");
            checks.Add("text-only recovery replaces evicted audio "+theme);
            Activate(false);Require(!playback.HasAudio&&playback.State==HistoryPlaybackState.Empty,"Leaving History retained its loaded audio.");
            Activate(true);Layout(page,448,420);
            var viewport=(ScrollViewer)page.GetType().GetProperty("Content")!.GetValue(page)!;
            Require(viewport.ExtentHeight>viewport.ViewportHeight,"Compact History cannot scroll to its player/actions.");
            Save(page,448,420,Path.Combine(output,"integrated-history-compact-"+theme+".png"));
            checks.Add("compact History scroll and selection lifetime "+theme);
            ((IDisposable)page).Dispose();
            Require(!playback.HasAudio,"Closing the main window retained History audio.");
            Activate(true);Require(!playback.HasAudio,"Disposed History reloaded protected audio.");
            checks.Add("embedded History dispose is final "+theme);
            Set(controller,"inserting",new HashSet<string>());Set(controller,"destinations",new Dictionary<string,LocalDictation.App.Services.ITextDelivery>());
            Set(controller,"canonicalReviews",new Dictionary<string,ReviewDocument>{{first.Id,first}});
            var details=new ReviewWindow(first,controller.SaveReview,controller.DismissReview,_=>Task.FromResult(new ReviewInsertionResult(false,"Synthetic")),_=>{});
            var closed=false;details.Closed+=(_,_)=>closed=true;Set(controller,"reviewWindow",details);
            try
            {
                Require(new System.Windows.Interop.WindowInteropHelper(details).Handle==0,"Deletion fixture created a native window.");
                controller.DeleteDictation(first.Id);Require(closed&&!controller.Dictations.Any(i=>i.Id==first.Id),"Deleting a dictation left its expanded editor alive.");
                checks.Add("delete closes that dictation editor "+theme);
            }
            finally{details.Close();}

        }
        finally{(page as IDisposable)?.Dispose();}
    }
    static void Layout(FrameworkElement root,int width,int height){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
    static void Save(FrameworkElement root,int width,int height,string path)
    {var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);}
}
