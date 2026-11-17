using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.Core;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.App.Services;

public static class HistorySearchChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Get<T>(object x,string n)=>(T)x.GetType().GetField(n,Fields)!.GetValue(x)!;
    static void Set(object x,string n,object? v)=>x.GetType().GetField(n,Fields)!.SetValue(x,v);
    static object? Call(object target,string name)=>target.GetType().GetMethod(name,Fields)!.Invoke(target,null);
    static void Require(bool x,string why){if(!x)throw new InvalidOperationException(why);}
    static void Layout(FrameworkElement p,int width){p.Measure(new(width,820));p.Arrange(new Rect(0,0,width,820));p.UpdateLayout();SettingsHelpChecks.SettleForRender(p);}
    public static void Run(string output,List<string> checks)
    {
        var previous=ThemeManager.Choice;
        try{foreach(var theme in new[]{"Light","Dark"})Check(theme,output,checks);}finally{ThemeManager.Apply(previous);}
    }
    static void Check(string theme,string output,List<string> checks)
    {
        ThemeManager.Apply(theme);
        var first=new ReviewDocument{Created=DateTimeOffset.Now,PrimaryModel="first"};first.SetPrimary(new string('x',100)+" hidden napätie");
        var second=new ReviewDocument{Created=first.Created.AddDays(-1),PrimaryModel="archived/model"};second.SetPrimary("saved text only");
        var store=new HistoryStore(Path.Combine(output,"search-fixture",Guid.NewGuid().ToString("N")));store.Add(first,new byte[32000]);store.Add(second,new byte[32000]);
        var controller=ReviewWorkflowChecks.Controller(output,first);Set(controller,"historyStore",store);Set(controller,"<History>k__BackingField",new List<HistoryEntry>{new(first,1),new(second,1){HasAudio=false}});
        Set(controller,"<Models>k__BackingField",null); // History remains usable before a model catalog is available.
        using var panel=new HistoryPanel(controller);Layout(panel,800);panel.SetActive(true);Layout(panel,800);
        var list=Get<ListBox>(panel,"items");var search=Get<TextBox>(panel,"historySearch");var transcript=Get<TextBox>(panel,"transcript");
        Require((list.SelectedItem as DictationItem)?.Id==first.Id,"Initial History selection missing.");
        var filters=Get<Button>(panel,"filterButton");var surface=Get<Border>(panel,"filterSurface");
        Require(surface.Background is SolidColorBrush background&&background.Color==((SolidColorBrush)panel.FindResource("SurfaceContainerHigh")).Color,"Filter popover lost its distinct themed surface.");
        Require(!surface.IsDescendantOf(panel),"Filter form still occupies History layout space.");
        foreach(var field in new[]{"needsReview","textOnly","inserted"})
            Require(Get<CheckBox>(panel,field).Foreground is SolidColorBrush checkInk&&checkInk.Color==((SolidColorBrush)panel.FindResource("Ink")).Color,"Filter labels are unreadable in the active theme.");
        search.Text="HIDDEN NAPATIE";Require(list.Items.Count==1&&transcript.Text==first.Primary,"Search misses full text or strips stored accents.");
        search.Text="no match";Require(list.Items.Count==0&&list.SelectedItem==null&&transcript.Text.Contains("No dictations match"),"No-match search retains a hidden selected transcript.");
        var player=Get<HistoryAudioPlayerControl>(panel,"playback");Require(player.Visibility==Visibility.Collapsed,"Filtered-out recording keeps the player visible.");
        search.Clear();Require((list.SelectedItem as DictationItem)?.Id==first.Id,"Clearing the query did not restore selected identity.");
        Call(panel,"PrepareFilterDraft");Get<CheckBox>(panel,"textOnly").IsChecked=true;Call(panel,"ApplyFilterDraft");Require(list.Items.Count==1&&list.SelectedItem==null,"Text-only filter auto-selects a different recording.");
        Get<CheckBox>(panel,"textOnly").IsChecked=false;Call(panel,"ApplyFilterDraft");Require((list.SelectedItem as DictationItem)?.Id==first.Id,"Filter reset lost selected identity.");
        Get<DatePicker>(panel,"fromDate").SelectedDate=DateTime.Today.AddDays(2);Get<DatePicker>(panel,"toDate").SelectedDate=DateTime.Today;Call(panel,"ApplyFilterDraft");
        Require(!string.IsNullOrEmpty(Get<TextBlock>(panel,"filterError").Text),"Reversed date range has no visible error.");
        Get<DatePicker>(panel,"fromDate").SelectedDate=Get<DatePicker>(panel,"toDate").SelectedDate=null;Call(panel,"ApplyFilterDraft");
        foreach(var width in new[]{432,640,1040})
        {
            Layout(panel,width);var inputBounds=search.TransformToAncestor(panel).TransformBounds(new Rect(search.RenderSize));var filterBounds=filters.TransformToAncestor(panel).TransformBounds(new Rect(filters.RenderSize));Require(Math.Abs(inputBounds.Top-filterBounds.Top)<1&&inputBounds.Right<=filterBounds.Left,"Filters is not compactly aligned beside search.");var image=new RenderTargetBitmap(width,820,96,96,PixelFormats.Pbgra32);image.Render(panel);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,$"history-search-{theme}-{width}.png"));png.Save(file);
        }
        surface.Measure(new(400,double.PositiveInfinity));surface.Arrange(new Rect(0,0,400,surface.DesiredSize.Height));surface.UpdateLayout();SettingsHelpChecks.SettleForRender(surface);
        var expandedImage=new RenderTargetBitmap(400,(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);expandedImage.Render(surface);var expandedPng=new PngBitmapEncoder();expandedPng.Frames.Add(BitmapFrame.Create(expandedImage));using(var expandedFile=File.Create(Path.Combine(output,$"history-filter-form-{theme}.png")))expandedPng.Save(expandedFile);
        Set(controller,"<PrivateNextRecording>k__BackingField",true);
        Require(controller.StartAudioImport(["not-opened.wav"])?.Contains("Private")==true,"An ordinary import can consume private-next.");
        Require(controller.ImportItems.Count==0&&Get<Dictionary<string,ITextDelivery>>(controller,"destinations").Count==0,"Blocked import acquired a destination or queue.");
        var audio=Get<HistoryAudioPlayback>(player,"playback");Require(Get<object?>(audio,"output")==null&&PresentationSource.FromVisual(panel)==null,"Search/import checks opened audio output or a native window.");
        checks.Add("History full-content search, filters, selection restoration, private import guard and no device/window "+theme);
    }
}
