using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

public static class HistoryRetentionChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {if(root is T match)yield return match;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void Layout(FrameworkElement root,int width){root.Measure(new(width,820));root.Arrange(new Rect(0,0,width,820));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);}
    public static void Run(string output,List<string> checks)
    {
        var previous=ThemeManager.Choice;
        try{foreach(var theme in new[]{"Light","Dark"})Check(theme,output,checks);}finally{ThemeManager.Apply(previous);}
    }
    static void Check(string theme,string output,List<string> checks)
    {
        ThemeManager.Apply(theme);
        var old=new ReviewDocument{Created=DateTimeOffset.UtcNow.AddDays(-10)};old.SetPrimary("Synthetic saved text remains after recording expiry.");
        var recent=new ReviewDocument{Created=DateTimeOffset.UtcNow.AddHours(-2)};recent.SetPrimary("Synthetic recent recording.");
        var store=new HistoryStore(Path.Combine(output,"retention-fixture",Guid.NewGuid().ToString("N")));store.Add(old,new byte[32000]);store.Add(recent,new byte[32000]);
        var owner=ReviewWorkflowChecks.Controller(output,old);Set(owner,"historyStore",store);Set(owner,"<History>k__BackingField",store.Load());Set(owner,"noHotkey",true);
        var window=(SettingsWindow)typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!.Invoke([owner,false]);
        var shell=(MainWindowShell)window.Content;window.Content=null;
        var frame=new Border{Child=shell};frame.SetResourceReference(Border.BackgroundProperty,"Canvas");window.Content=frame;
        try
        {
            var retention=Get<ComboBox>(window,"historyRetention");
            Require((int)retention.SelectedValue==7,"The retention setting does not show its seven-day default.");
            Require(retention.Items.Cast<KeyValuePair<int,string>>().Any(c=>c.Key==0&&c.Value=="Forever"),"Retention offers no explicit Forever choice.");
            var before=JsonSerializer.Serialize(owner.Settings);
            shell.Search.Query="audio retention";
            var entry=Get<ListBox>(shell.Search,"results").Items.Cast<SettingsSearchEntry>().SingleOrDefault(e=>e.Id=="history-retention");
            Require(entry!=null,"Retention cannot be found through settings search.");
            typeof(SettingsWindow).GetMethod("OpenSearchSetting",Fields)!.Invoke(window,[entry]);
            Require(shell.Navigation.SelectedIndex==3&&!shell.Search.IsQueryActive&&JsonSerializer.Serialize(owner.Settings)==before,"Retention search changed the policy or missed Settings.");
            foreach(var width in new[]{640,1040})
            {
                Layout(frame,width);
                var enable=Find<TextBlock>(frame).Single(t=>t.Text=="Enable");var title=Find<TextBlock>(frame).Single(t=>t.Text=="Keyboard shortcut");
                double Center(FrameworkElement e)=>e.TransformToAncestor(frame).Transform(new Point(0,e.ActualHeight/2)).Y;
                Require(enable.FontSize==title.FontSize&&enable.FontWeight==title.FontWeight&&Math.Abs(Center(enable)-Center(title))<1,"Enable does not match the Keyboard shortcut heading size/weight/vertical alignment.");
                var ink=((SolidColorBrush)frame.FindResource("Ink")).Color;
                Require(new[]{Get<RadioButton>(window,"shortcutPress"),Get<RadioButton>(window,"shortcutHold")}.All(r=>r.Foreground is SolidColorBrush brush&&brush.Color==ink),"Recording mode labels do not follow the theme's readable foreground.");
                var bitmap=new RenderTargetBitmap(width,820,96,96,PixelFormats.Pbgra32);bitmap.Render(frame);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,$"history-retention-{theme}-{width}.png"));png.Save(file);
            }
            checks.Add("History retention default/search and Enable heading size/alignment "+theme);
            retention.SelectedValue=1;
            Require(owner.Settings.AudioRetentionDays==1&&Settings.Load().AudioRetentionDays==1,"Retention selection did not persist.");
            Require(owner.History.Count==2&&!owner.History.Single(e=>e.Id==old.Id).HasAudio&&owner.History.Single(e=>e.Id==recent.Id).HasAudio,"Changing retention did not expire only the old recording.");
            Require(owner.Dictations.Single(e=>e.Id==old.Id) is {HasAudio:false,DurationLabel:"Text only"} row&&row.Document.OutputText==old.OutputText,"Cleanup hid text or left the library row playable.");
            Require(!owner.CanVerifyReview(old),"An expired recording remains eligible for retranscription.");
            retention.SelectedValue=0;
            Require(owner.Settings.AudioRetentionDays==0&&!owner.History.Single(e=>e.Id==old.Id).HasAudio,"Forever selection lost persistence or resurrected removed audio.");
            Require(new WindowInteropHelper(window).Handle==0,"Retention checks opened a native window.");
            checks.Add("Retention settings persist, expire only audio and retain text-only history without native input/audio "+theme);
        }
        finally{window.Close();}
    }
}
