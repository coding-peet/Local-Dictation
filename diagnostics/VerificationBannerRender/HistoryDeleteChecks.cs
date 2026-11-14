using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class HistoryDeleteChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Property(object target,string name,object? value)=>Set(target,"<"+name+">k__BackingField",value);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {if(root is T value)yield return value;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    public static void Run(string output,List<string> checks)
    {
        var directory=Path.Combine(output,"delete-fixture",Guid.NewGuid().ToString("N"));
        var store=new HistoryStore(directory);
        var first=new ReviewDocument();first.SetPrimary("Keep this synthetic transcript.");
        var empty=new ReviewDocument{Created=first.Created.AddMinutes(-1)};
        var textOnly=new ReviewDocument{Created=first.Created.AddMinutes(-2)};textOnly.SetPrimary("Pending text-only synthetic review.");
        store.Add(first,new byte[32000]);store.Add(empty,new byte[32000]);
        var pendingDirectory=Path.Combine(directory,"pending");var pending=new PendingReviews(new ReviewStore(pendingDirectory));pending.Save(empty);pending.Save(textOnly);
        var owner=ReviewWorkflowChecks.Controller(output,first);
        Set(owner,"historyStore",store);Set(owner,"pending",pending);Property(owner,"History",store.Load());
        Set(owner,"canonicalReviews",new Dictionary<string,ReviewDocument>{{first.Id,first},{empty.Id,empty},{textOnly.Id,textOnly}});
        using var panel=new HistoryPanel(owner);panel.SetActive(true);
        try
        {
            void Layout(){panel.Measure(new Size(760,560));panel.Arrange(new Rect(0,0,760,560));panel.UpdateLayout();}
            Layout();var list=Find<ListBox>(panel).Single();
            Button DeleteFor(string id)
            {
                Layout();var item=list.Items.Cast<DictationItem>().Single(i=>i.Id==id);
                var row=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
                var button=Find<Button>(row).SingleOrDefault(b=>AutomationProperties.GetName(b).StartsWith("Delete dictation"));
                Require(button!=null,"History rows are missing their small delete action.");return button!;
            }
            var cross=DeleteFor(empty.Id);Require(cross.ActualWidth>=48&&cross.ActualHeight>=48,"The row delete action lost its usable interaction target.");
            var confirm=typeof(HistoryPanel).GetField("confirmDelete",Fields);Require(confirm!=null,"Row and More deletion do not share injectable confirmation.");
            var player=Find<HistoryAudioPlayerControl>(panel).Single();
            var audio=(HistoryAudioPlayback)typeof(HistoryAudioPlayerControl).GetField("playback",Fields)!.GetValue(player)!;
            audio.Seek(TimeSpan.FromMilliseconds(250));
            var confirmations=0;
            confirm!.SetValue(panel,(Func<DictationItem,bool>)(entry=>{confirmations++;Require(entry.Id==empty.Id,"Confirmation targets selected rather than clicked row.");return false;}));
            cross.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(confirmations==1&&owner.Dictations.Count==3&&File.Exists(Path.Combine(directory,empty.Id+".audio")),"Cancelling row deletion changed stored data.");
            Require((list.SelectedItem as DictationItem)?.Id==first.Id&&audio.HasAudio&&audio.Position==TimeSpan.FromMilliseconds(250),"Cancelling deletion interrupted unrelated selected audio.");
            confirm.SetValue(panel,(Func<DictationItem,bool>)(_=>{Set(owner,"inserting",new HashSet<string>{first.Id});return true;}));
            DeleteFor(empty.Id).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(owner.Dictations.Count==3&&audio.HasAudio&&audio.Position==TimeSpan.FromMilliseconds(250),"A modal operation change bypassed deletion guards or cleared unrelated playback.");
            Set(owner,"inserting",new HashSet<string>());
            list.SelectedItem=list.Items.Cast<DictationItem>().Single(i=>i.Id==empty.Id);
            confirm.SetValue(panel,(Func<DictationItem,bool>)(entry=>{Require(entry.Id==empty.Id,"Wrong row was offered for deletion.");list.SelectedItem=list.Items.Cast<DictationItem>().Single(i=>i.Id==first.Id);audio.Seek(TimeSpan.FromMilliseconds(375));return true;}));
            DeleteFor(empty.Id).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(owner.Dictations.Count==2&&owner.Dictations.All(i=>i.Id!=empty.Id)&&owner.Dictations.Any(i=>i.Id==first.Id)&&owner.Dictations.Any(i=>i.Id==textOnly.Id),"Confirmed row deletion removed the wrong item or left pending recovery.");
            Require((list.SelectedItem as DictationItem)?.Id==first.Id&&audio.HasAudio&&audio.Position==TimeSpan.FromMilliseconds(375),"Deleting another row interrupted the newly selected recording.");
            Require(!File.Exists(Path.Combine(directory,empty.Id+".audio"))&&!File.Exists(Path.Combine(directory,empty.Id+".history"))&&!File.Exists(Path.Combine(pendingDirectory,empty.Id+".review")),"Confirmed deletion left audio, history or Review storage behind.");
            confirm.SetValue(panel,(Func<DictationItem,bool>)(_=>true));DeleteFor(textOnly.Id).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(owner.Dictations.Count==1&&owner.Dictations.Single().Id==first.Id&&!File.Exists(Path.Combine(pendingDirectory,textOnly.Id+".review")),"Text-only pending deletion did not remove only its own entry.");
            var menu=(ContextMenu)typeof(HistoryPanel).GetField("moreMenu",Fields)!.GetValue(panel)!;
            var moreDelete=menu.Items.OfType<MenuItem>().Single(i=>i.Header?.ToString()=="Delete…");
            confirm.SetValue(panel,(Func<DictationItem,bool>)(entry=>{Require(entry.Id==first.Id,"More Delete did not share row confirmation.");return false;}));
            moreDelete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(owner.Dictations.Count==1&&audio.HasAudio,"Cancelling More Delete changed the dictation.");
            confirm.SetValue(panel,(Func<DictationItem,bool>)(_=>true));moreDelete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Layout();
            Require(owner.Dictations.Count==0&&list.Items.Count==0&&Find<TextBlock>(panel).Any(t=>t.Text=="0 items")&&Find<TextBlock>(panel).Any(t=>t.Text=="No dictations yet"),"Deleting the final dictation lost the empty state or count.");
            Require(Find<TextBox>(panel).Single().Text=="Record a dictation to see its transcript and original audio here."&&player.Visibility==Visibility.Collapsed&&!audio.HasAudio,"Empty History does not explain the next step or retains audio.");
            Require(Find<Button>(panel).Where(b=>new[]{"Open Review","Copy text","More actions"}.Contains(AutomationProperties.GetName(b))).All(b=>!b.IsEnabled),"Empty History keeps detail actions enabled.");
            checks.Add("History row and More delete confirmation, cancellation, stable clicked ID, busy guard, selected audio lifetime, empty/audio/pending/text-only removal and zero-item guidance with synthetic stores");
        }
        finally{panel.SetActive(false);}
    }
}
