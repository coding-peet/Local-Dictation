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
        var pending=new PendingReviews(new ReviewStore(Path.Combine(directory,"pending")));Set(controller,"pending",pending);pending.Save(first);
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
            Save(page,760,560,Path.Combine(output,"layout-metrics-"+theme+".png"));
            Require(transcript.ActualHeight>=300,$"The selected transcript still loses height to page chrome and actions: text={transcript.ActualHeight}, player={player.ActualHeight}, page={page.ActualHeight}.");
            var open=Find<Button>(page).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Open Review");
            Require(open.TransformToAncestor(page).Transform(new Point()).X>=transcript.TransformToAncestor(page).Transform(new Point()).X-24,"Review actions are still detached from their selected text.");
            Require(Find<Button>(page).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="More actions"),"Secondary dictation actions are not grouped in More.");
            checks.Add("detail-focused text space and actions "+theme);
            var resources=Application.Current.Resources;var oldAccent=resources["Accent"];var oldSelected=resources["Selected"];var oldInk=resources["SelectedInk"];var normalTemplate=list.ItemTemplate;
            try
            {
                resources["Accent"]=SystemColors.HighlightBrush;resources["Selected"]=SystemColors.HighlightBrush;resources["SelectedInk"]=SystemColors.HighlightTextBrush;
                // A detached tree has no Window resource invalidation broadcast;
                // construct its row template under the simulated selection palette.
                list.ItemTemplate=(DataTemplate)page.GetType().GetMethod("RowTemplate",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
                Layout(page,760,560);
                var container=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
                Require(Find<TextBlock>(container).Where(t=>t.Text.Length>0&&t.Visibility==Visibility.Visible).All(t=>t.Foreground is SolidColorBrush brush&&brush.Color==SystemColors.HighlightTextColor),"Selected History text ignores the high-contrast selection foreground: selected="+container.IsSelected+", expected="+SystemColors.HighlightTextColor+", actual="+string.Join(";",Find<TextBlock>(container).Where(t=>t.Text.Length>0&&t.Visibility==Visibility.Visible).Select(t=>t.Text[..Math.Min(10,t.Text.Length)]+"="+t.Foreground)));
                checks.Add("selected History text retains high contrast "+theme);
            }
            finally{resources["Accent"]=oldAccent;resources["Selected"]=oldSelected;resources["SelectedInk"]=oldInk;list.ItemTemplate=normalTemplate;Layout(page,760,560);}

            Save(page,760,560,Path.Combine(output,"integrated-history-wide-"+theme+".png"));
            checks.Add("integrated main History content "+theme);
            var ui=typeof(SettingsWindow).Assembly.GetType("LocalDictation.App.Views.Ui")!;
            Button Button(string label,bool primary=false)=>(Button)ui.GetMethod("Button",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,[label,(Action)(()=>{}),primary])!;
            TextBlock Text(string label)=>(TextBlock)ui.GetMethod("Text",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,[label,12d,false])!;
            var nav=new ListBox();var record=Button("Record in banner",true);var help=Button("Help");
            var connection=Text("Ready · Local transcription");var saved=Text("Saved automatically");var alert=Text("");
            using(var shell=new MainWindowShell(nav,[new Border(),new Border(),page,new Border()],record,help,connection,saved,alert))
            {
                try
                {
                    nav.SelectedIndex=2;Layout(shell,1040,760);
                    Require(shell.Header.ActualHeight<=72&&shell.StatusArea.Visibility==Visibility.Collapsed,"Normal chrome did not collapse to a compact header.");
                    Require(transcript.ActualHeight>=380,"The selected design did not allocate the gained window height to the transcript.");
                    Require(nav.Items.OfType<ListBoxItem>().Select(i=>(TextBlock)i.Content).All(t=>t.Visibility==Visibility.Visible)&&nav.ActualWidth>=144,"Named navigation was hidden or collapsed.");
                    Save(shell,1040,760,Path.Combine(output,"named-menu-shell-wide-"+theme+".png"));checks.Add("named menu and compact real main chrome "+theme);
                    Layout(shell,640,600);
                    Require(nav.Items.OfType<ListBoxItem>().Select(i=>(TextBlock)i.Content).All(t=>t.Visibility==Visibility.Visible),"Narrow window hides its menu labels.");
                    Save(shell,640,600,Path.Combine(output,"named-menu-shell-compact-"+theme+".png"));checks.Add("named menu retained at compact width "+theme);
                    alert.Text="Windows could not restore the previous clipboard yet. Its backup is kept and restoration will retry automatically. A newer copy takes priority.";
                    connection.Text="The local transcription service is unavailable. Reconnect after checking its status.";saved.Text="Settings could not be saved. The previous valid settings are still active.";
                    Layout(shell,640,600);
                    Require(shell.StatusArea.Visibility==Visibility.Visible&&alert.ActualHeight>0&&saved.ActualHeight>0,"Compact chrome hid actionable errors.");
                    Require(!Find<Button>(shell).Any(b=>b.Content?.ToString()?.Contains("clipboard",StringComparison.OrdinalIgnoreCase)==true),"A clipboard recovery action remains in the main window.");
                    Save(shell,640,600,Path.Combine(output,"named-menu-shell-errors-"+theme+".png"));checks.Add("compact save and recovery errors remain visible "+theme);
                }
                finally{shell.PageHost.Content=null;}
            }

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
