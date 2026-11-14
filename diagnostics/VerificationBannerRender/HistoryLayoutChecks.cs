using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

public static class HistoryLayoutChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Property(object target,string name,object? value)=>Set(target,"<"+name+">k__BackingField",value);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {if(root is T match)yield return match;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void Layout(FrameworkElement root,int width,int height=560){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
    static void Save(FrameworkElement root,int width,int height,string path)
    {var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);}
    public static void Run(string output,List<string> checks)
    {
        var originalTheme=ThemeManager.Choice;
        try{foreach(var theme in new[]{"Light","Dark"})Check(theme,output,checks);}
        finally{ThemeManager.Apply(originalTheme);}
    }
    static void CheckConfirmationGuard(HistoryPanel panel,AppController controller,DictationItem entry)
    {
        var show=typeof(HistoryPanel).GetField("showDeleteConfirmation",Fields);
        Require(show!=null,"History has no synthetic boundary for testing modal shortcut suppression.");
        var original=show!.GetValue(panel);var confirm=typeof(HistoryPanel).GetMethod("ConfirmDelete",Fields)!;
        var input=(GlobalInput)typeof(AppController).GetField("input",Fields)!.GetValue(controller)!;var calls=0;
        try
        {
            show.SetValue(panel,(Func<Window?,DictationItem,bool>)((_,item)=>{calls++;Require(item.Id==entry.Id&&input.CapturingShortcut,"The delete dialog opens without suppressing global shortcuts.");return false;}));
            Require(!(bool)confirm.Invoke(panel,[entry])!&&calls==1&&!input.CapturingShortcut,"Cancelling delete confirmation leaves global shortcuts suppressed.");
            var active=typeof(AppController).GetField("active",Fields)!;active.SetValue(controller,RuntimeHelpers.GetUninitializedObject(active.FieldType));
            try{Require(!(bool)confirm.Invoke(panel,[entry])!&&calls==1&&!input.CapturingShortcut,"Busy History opens a delete dialog or changes shortcut capture.");}
            finally{active.SetValue(controller,null);}
            show.SetValue(panel,(Func<Window?,DictationItem,bool>)((_,_)=>throw new InvalidOperationException("Synthetic dialog failure")));
            var threw=false;try{confirm.Invoke(panel,[entry]);}catch(TargetInvocationException error) when(error.InnerException?.Message=="Synthetic dialog failure"){threw=true;}
            Require(threw&&!input.CapturingShortcut,"A failed delete dialog leaves global shortcuts suppressed.");
        }
        finally{show.SetValue(panel,original);((TextBlock)typeof(HistoryPanel).GetField("message",Fields)!.GetValue(panel)!).Text="";}
    }
    static void Check(string theme,string output,List<string> checks)
    {
        ThemeManager.Apply(theme);
        var directory=Path.Combine(output,"history-layout-fixture",Guid.NewGuid().ToString("N"));var store=new HistoryStore(directory);
        var first=new ReviewDocument();first.SetPrimary(string.Concat(Enumerable.Repeat("Synthetic long transcript for resizing the History detail and checking narrow scrollbars. ",100)));
        store.Add(first,new byte[64000]);
        for(var i=1;i<20;i++){var document=new ReviewDocument{Created=first.Created.AddMinutes(-i)};document.SetPrimary("Earlier synthetic dictation with a two-line preview for resize checks.");store.Add(document,new byte[64000]);}
        var controller=ReviewWorkflowChecks.Controller(output,first);Set(controller,"historyStore",store);Property(controller,"History",store.Load());
        using var panel=new HistoryPanel(controller);Layout(panel,760);panel.SetActive(true);Layout(panel,760);
        var splitter=Find<GridSplitter>(panel).SingleOrDefault();Require(splitter!=null,"History is missing the draggable list/detail divider.");
        splitter!.ApplyTemplate();
        Require(splitter.Template.FindName("Grip",splitter) is Border grip&&grip.Opacity==0,"History divider still shows a permanent idle line.");
        var body=(Grid)splitter!.Parent;var list=Find<ListBox>(panel).Single();var text=Find<TextBox>(panel).Single();
        var player=Find<HistoryAudioPlayerControl>(panel).Single();var playback=(HistoryAudioPlayback)typeof(HistoryAudioPlayerControl).GetField("playback",Fields)!.GetValue(player)!;
        var audioField=typeof(HistoryAudioPlayback).GetField("audio",Fields)!;var outputField=typeof(HistoryAudioPlayback).GetField("output",Fields)!;
        var originalAudio=audioField.GetValue(playback);var originalPeaks=playback.Peaks;playback.Seek(TimeSpan.FromMilliseconds(375));
        CheckConfirmationGuard(panel,controller,(DictationItem)list.SelectedItem);
        Require(body.ColumnDefinitions.Count==3&&Math.Abs(body.ColumnDefinitions[0].ActualWidth-212)<1&&splitter.ActualWidth is >=8 and <=12,"History did not retain the 212-DIP default list and a narrow divider.");
        Require(splitter.Focusable&&splitter.KeyboardIncrement>0&&AutomationProperties.GetName(splitter)=="Resize dictations list"&&AutomationProperties.GetHelpText(splitter).Contains("Left and Right"),"History resizing lacks an accessible keyboard target or instructions.");
        void Stable()
        {
            Require((list.SelectedItem as DictationItem)?.Id==first.Id&&text.Text==first.OutputText&&ReferenceEquals(originalAudio,audioField.GetValue(playback))&&ReferenceEquals(originalPeaks,playback.Peaks)&&playback.Position==TimeSpan.FromMilliseconds(375),"History resize changed the selection, transcript or loaded recording.");
            Require(outputField.GetValue(playback)==null&&PresentationSource.FromVisual(panel)==null,"History layout verification opened audio output or a native window.");
        }
        void Bounds()
        {
            Require(body.ColumnDefinitions[0].ActualWidth>=159.9&&body.ColumnDefinitions[2].ActualWidth>=239.9,"History column resizing violated a list/detail minimum.");
            Require(body.ColumnDefinitions.Sum(c=>c.ActualWidth)<=body.ActualWidth+.1,"History column widths overflow the available page.");Stable();
        }
        void Drag(double delta,int width=760)
        {
            splitter.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
            splitter.RaiseEvent(new DragDeltaEventArgs(delta,0){RoutedEvent=Thumb.DragDeltaEvent});Layout(panel,width);
            splitter.RaiseEvent(new DragCompletedEventArgs(delta,0,false){RoutedEvent=Thumb.DragCompletedEvent});Layout(panel,width);Bounds();
        }
        var baseline=new ScrollViewer{Content=new TextBlock{Text=first.OutputText,TextWrapping=TextWrapping.Wrap},VerticalScrollBarVisibility=ScrollBarVisibility.Visible};
        var baselineRoot=new Grid();baselineRoot.Children.Add(baseline);Layout(baselineRoot,200,100);baseline.ApplyTemplate();Layout(baselineRoot,200,100);
        var baselineTemplate=Find<ScrollBar>(baseline).First(b=>b.Orientation==Orientation.Vertical).Template;
        var bars=Find<ScrollBar>(panel).Where(b=>b.Orientation==Orientation.Vertical&&b.Visibility==Visibility.Visible&&b.ActualHeight>0).ToArray();
        Require(bars.Length>=2&&bars.All(b=>Math.Abs(b.ActualWidth-10)<.1),"History vertical scrollbars are not scoped to 10 DIPs.");
        Require(bars.All(b=>ReferenceEquals(b.Template,baselineTemplate)),"Thin History scrollbars replaced the existing scrollbar template.");
        var horizontalViewer=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Visible,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=new Border{Width=1000,Height=10}};
        var horizontalHost=new Grid();foreach(var key in panel.Resources.Keys)horizontalHost.Resources[key]=panel.Resources[key];horizontalHost.Children.Add(horizontalViewer);Layout(horizontalHost,200,60);horizontalViewer.ApplyTemplate();Layout(horizontalHost,200,60);
        var horizontal=Find<ScrollBar>(horizontalViewer).First(b=>b.Orientation==Orientation.Horizontal);
        Require(Math.Abs(horizontal.ActualHeight-10)<.1&&horizontal.Template!=null,"The scoped horizontal History scrollbar is not 10 DIPs or lost its template.");
        Save(panel,760,560,Path.Combine(output,"history-layout-default-"+theme+".png"));
        Drag(100);Require(Math.Abs(body.ColumnDefinitions[0].ActualWidth-312)<1,"Synthetic divider drag did not resize the recording list.");
        Save(panel,760,560,Path.Combine(output,"history-layout-dragged-"+theme+".png"));
        Drag(-1000);Require(Math.Abs(body.ColumnDefinitions[0].ActualWidth-160)<1,"History list did not stop at its minimum width.");
        Drag(10000);var preferred=body.ColumnDefinitions[0].ActualWidth;Require(Math.Abs(body.ColumnDefinitions[2].ActualWidth-240)<1,$"History detail did not stop at its minimum width: body={body.ActualWidth}, list={body.ColumnDefinitions[0].ActualWidth}, preferred={body.ColumnDefinitions[0].Width}, divider={body.ColumnDefinitions[1].ActualWidth}, detail={body.ColumnDefinitions[2].ActualWidth}.");
        Layout(panel,660);Bounds();Require(body.ColumnDefinitions[0].ActualWidth<preferred,"Narrowing a wide History page did not clamp the recording list.");
        Layout(panel,1000);Bounds();Require(Math.Abs(body.ColumnDefinitions[0].ActualWidth-preferred)<1,"A temporary resize overwrote the user's preferred list width.");
        Drag(-180,1000);preferred=body.ColumnDefinitions[0].ActualWidth;
        Layout(panel,448,420);Stable();
        Require(splitter.Visibility==Visibility.Collapsed&&body.ColumnDefinitions[1].ActualWidth<.1&&body.ColumnDefinitions[2].ActualWidth<.1,"Compact History retains its divider or side-by-side column constraints.");
        var outer=(ScrollViewer)panel.Content;Require(outer.ExtentHeight>outer.ViewportHeight,"Stacked History lost vertical scrolling.");
        bars=Find<ScrollBar>(panel).Where(b=>b.Orientation==Orientation.Vertical&&b.Visibility==Visibility.Visible&&b.ActualHeight>0).ToArray();
        Require(bars.All(b=>Math.Abs(b.ActualWidth-10)<.1),"The compact page's outer scrollbar is wider than its History controls.");
        Save(panel,448,420,Path.Combine(output,"history-layout-stacked-"+theme+".png"));
        Layout(panel,760);Bounds();Require(splitter.Visibility==Visibility.Visible&&Math.Abs(body.ColumnDefinitions[0].ActualWidth-preferred)<1,"Returning from stacked History reset the user's column width.");
        var row=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);var cross=Find<Button>(row).Single(b=>AutomationProperties.GetName(b).StartsWith("Delete dictation"));
        Require(cross.ActualWidth>=48&&cross.ActualHeight>=48,"History resizing shrank a row's delete target.");
        Save(panel,760,560,Path.Combine(output,"history-layout-restored-"+theme+".png"));
        panel.SetActive(false);Require(!playback.HasAudio&&playback.State==HistoryPlaybackState.Empty,"Leaving resized History retained its recording.");
        panel.Dispose();panel.SetActive(true);Require(!playback.HasAudio,"Disposed resized History reloaded audio.");
        checks.Add("History delete modal suppresses shortcuts, refuses busy operations and restores capture after dialog failure "+theme);
        checks.Add("History scoped thin scrollbars retain their native template "+theme);
        checks.Add("History divider drag, min bounds, window clamping and stacked width restoration "+theme);
        checks.Add("History resize retains selected transcript/audio and row actions without output hardware "+theme);
    }
}
