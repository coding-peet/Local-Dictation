using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

public static class ShellPlacementChecks
{
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {if(root is T match)yield return match;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static Rect Bounds(FrameworkElement element,Visual ancestor)=>element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
    static void Layout(FrameworkElement root,int width,int height){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
    static void Save(FrameworkElement root,int width,int height,string path)
    {
        SettingsHelpChecks.SettleForRender(root);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    public static void Run(string output,List<string> checks)
    {
        var originalTheme=ThemeManager.Choice;
        try{foreach(var theme in new[]{"Light","Dark"})Check(theme,output,checks);}
        finally{ThemeManager.Apply(originalTheme);}
    }
    static void Check(string theme,string output,List<string> checks)
    {
        ThemeManager.Apply(theme);
        var ui=typeof(MainWindowShell).Assembly.GetType("LocalDictation.App.Views.Ui")!;
        var factory=ui.GetMethod("Button",BindingFlags.Static|BindingFlags.Public)!;
        Button Button(string label,Action action,bool primary=false)=>(Button)factory.Invoke(null,[label,action,primary])!;
        var recordCalls=0;var helpCalls=0;var selected=new List<int>();
        var record=Button("Record",()=>recordCalls++,true);record.ToolTip="Start a recording using the selected settings.";
        var help=Button("Help",()=>helpCalls++);var helpName=AutomationProperties.GetName(help);
        var connection=new TextBlock{Text="Ready · Local transcription"};var saved=new TextBlock{Text="Saved automatically"};var alert=new TextBlock();
        saved.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var labels=new[]{"Dictation","Models","History","Settings"};
        var pages=labels.Select(label=>{var caption=new TextBlock{Text=label+" workspace",FontSize=20};caption.SetResourceReference(TextBlock.ForegroundProperty,"Ink");return (UIElement)new Border{Padding=new(16),Child=caption};}).ToArray();
        using var shell=new MainWindowShell(new ListBox(),pages,record,help,connection,saved,alert,selected.Add);
        shell.Search.SetEntries([new("model","Dictation","Dictation model","Choose the model used for speech recognition.")]);
        foreach(var (width,height) in new[]{(1040,760),(640,600)})
        {
            Layout(shell,width,height);
            Require(!Find<TextBlock>(shell).Any(t=>t.Text=="Local Dictation"),"The shell duplicates the native window title.");
            Require(shell.StatusBar.ActualHeight<=32.1&&Find<TextBlock>(shell.StatusBar).Contains(connection),"Normal status lost its compact bottom layout.");
            var recordBounds=Bounds(record,shell);var searchBounds=Bounds(shell.Search,shell);var navBounds=Bounds(shell.Navigation,shell);var helpBounds=Bounds(help,shell);var pageBounds=Bounds(shell.PageHost,shell);var statusBounds=Bounds(shell.StatusBar,shell);
            Require(searchBounds.Top<=8.1&&Math.Abs(searchBounds.Top-pageBounds.Top)<1,"Search and workspace do not start at the top of the client content.");
            Require(recordBounds.Left>=navBounds.Left&&recordBounds.Right<=pageBounds.Left&&recordBounds.Top>=navBounds.Bottom&&recordBounds.Bottom<=helpBounds.Top&&helpBounds.Bottom<=statusBounds.Top,"Record is not above Help in the navigation footer.");
            Require(searchBounds.Bottom<=navBounds.Top&&helpBounds.Right<=pageBounds.Left&&statusBounds.Top>=pageBounds.Bottom,"Search, named navigation, footer actions or status overlap the workspace.");
            Require(record.ActualHeight>=48&&record.ActualWidth>=48&&help.ActualHeight>=48&&help.ActualWidth>=48,"Moved Record or Help lost its 48-DIP target.");
            Require(shell.Navigation.ActualWidth>=144&&shell.Navigation.Items.Cast<ListBoxItem>().Select(i=>AutomationProperties.GetName(i)).SequenceEqual(labels),"Action placement changed or collapsed the four named destinations.");
            Require(saved.Visibility==Visibility.Visible&&Bounds(saved,shell).Top>=pageBounds.Bottom&&Find<TextBlock>(shell.StatusBar).Contains(saved),"Normal saving status is not in the bottom status bar.");
            Save(shell,width,height,Path.Combine(output,$"shell-placement-{width}-{theme}.png"));
        }
        record.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));help.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Require(recordCalls==1&&helpCalls==1&&record.Content?.ToString()=="Record"&&record.ToolTip?.ToString()=="Start a recording using the selected settings."&&AutomationProperties.GetName(help)==helpName&&help.ToolTip?.ToString()=="Help (F1)","Moving primary and Help actions replaced their injected content, metadata or callbacks.");
        record.Content="Stop";Layout(shell,640,600);Require(record.Content?.ToString()=="Stop","Shell overwrites the controller's dynamic Record/Stop label.");
        Require(selected.SequenceEqual(new[]{0})&&ReferenceEquals(shell.PageHost.Content,pages[0]),"Relocated actions navigate away from the selected page.");
        for(var index=0;index<4;index++)
        {
            shell.Navigation.SelectedIndex=index;Layout(shell,640,600);
            Require(ReferenceEquals(shell.PageHost.Content,pages[index])&&saved.Visibility==(index is 0 or 3?Visibility.Visible:Visibility.Collapsed),"Saving success is shown on an unrelated page or hidden from a settings page.");
        }
        shell.Search.Query="model";Layout(shell,640,600);
        Require(shell.PageHost.Visibility==Visibility.Hidden&&shell.Search.ResultsView.Visibility==Visibility.Visible&&saved.Visibility==Visibility.Collapsed,"Search does not overlay the selected page without routine saving chrome.");
        var textBefore=pages[3];saved.SetResourceReference(TextBlock.ForegroundProperty,"Warning");saved.Text="Not saved: select a valid option and retry.";Layout(shell,640,600);
        Require(saved.Visibility==Visibility.Visible&&saved.ActualHeight>0&&ReferenceEquals(shell.PageHost.Content,textBefore)&&shell.PageHost.Visibility==Visibility.Hidden,"Search hides an actionable saving error or replaces the current page.");
        Save(shell,640,600,Path.Combine(output,"shell-placement-search-error-"+theme+".png"));
        shell.Search.Clear();
        foreach(var error in new[]{"Not saved: the settings file could not be written.","Settings could not be saved. The previous valid settings remain active."})
        {
            saved.Text=error;
            for(var index=0;index<4;index++)
            {
                shell.Navigation.SelectedIndex=index;Layout(shell,640,600);
                Require(saved.Visibility==Visibility.Visible&&saved.ActualHeight>0&&Bounds(shell.StatusBar,shell).Top>=Bounds(shell.PageHost,shell).Bottom,"A page hides actionable saving errors or moves them back into the header.");
            }
        }
        shell.Navigation.SelectedIndex=2;saved.SetResourceReference(TextBlock.ForegroundProperty,"Muted");saved.Text="Saved automatically · Changes apply to the next recording.";Layout(shell,640,600);
        Require(saved.Visibility==Visibility.Collapsed,"Routine busy-state saving status leaks into History.");
        saved.Text="All changes saved";Require(saved.Visibility==Visibility.Collapsed,"Routine alternate saving status leaks into History.");
        saved.Text="Saving…";Require(saved.Visibility==Visibility.Collapsed,"Routine saving progress leaks into History.");
        shell.Navigation.SelectedIndex=3;Require(saved.Visibility==Visibility.Visible,"Settings hides its own saving progress.");
        saved.Text="";Require(saved.Visibility==Visibility.Collapsed,"An empty saving footer wastes page height.");
        alert.Text="The clipboard backup is kept while Windows retries restoration.";Layout(shell,640,600);
        Require(shell.StatusArea.Visibility==Visibility.Visible&&alert.ActualHeight>0,"Action placement hid the global actionable alert.");
        Require(AutomationProperties.GetLiveSetting(saved)==AutomationLiveSetting.Polite&&PresentationSource.FromVisual(shell)==null,"Saving lost its polite announcement or verification created a native window.");
        checks.Add("search-first shell, footer Record/Help, no duplicate title and compact bottom status/error visibility "+theme);
    }
}
