using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

public static class GeneralStartupChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const string Executable=@"C:\Program Files\Local Dictation\LocalDictation.exe";
    public sealed class Store:IWindowsStartupStore
    {
        public object? Value;public int Reads,Writes,Deletes;public bool FailWrite,FailDelete;
        public object? Read(){Reads++;return Value;}
        public void Write(string command){Writes++;if(FailWrite)throw new UnauthorizedAccessException("private failure detail");Value=command;}
        public void Delete(){Deletes++;if(FailDelete)throw new IOException("private failure detail");Value=null;}
    }
    public static Store Inject(AppController owner)
    {
        var store=new Store();typeof(AppController).GetField("startupRegistration",Fields)!.SetValue(owner,new WindowsStartupRegistration(store,Executable));return store;
    }
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static void Call(object target,string name)=>target.GetType().GetMethod(name,Fields,null,Type.EmptyTypes,null)!.Invoke(target,null);
    static IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
    {if(root is T found)yield return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void Layout(FrameworkElement root,int width){root.Measure(new(width,820));root.Arrange(new Rect(0,0,width,820));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);}
    static void Search(SettingsWindow window,MainWindowShell shell,string id,string query,string category)
    {
        shell.Search.Query=query;Layout(shell,1040);
        var entry=Find<ListBox>(shell.Search.ResultsView).Single().Items.Cast<SettingsSearchEntry>().Single(e=>e.Id==id);
        Require(entry.Category==category,"Search retained a stale category for "+id);
        typeof(SettingsWindow).GetMethod("OpenSearchSetting",Fields)!.Invoke(window,[entry]);
    }
    public static void Run(string output,List<string> checks)
    {
        var document=new ReviewDocument();document.SetPrimary("Synthetic saved result.");
        var owner=ReviewWorkflowChecks.Controller(output,document);var store=Inject(owner);
        Set(owner,"<History>k__BackingField",new List<HistoryEntry>{new(document,0){HasAudio=false}});
        var settings=JsonSerializer.Serialize(owner.Settings);var originalTheme=ThemeManager.Choice;
        var settingsPath=Path.Combine(AppPaths.Data,"settings.json");var settingsBytes=File.Exists(settingsPath)?File.ReadAllBytes(settingsPath):null;
        var ctor=typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!;
        var window=(SettingsWindow)ctor.Invoke([owner,false]);
        try
        {
            var shell=(MainWindowShell)window.Content;
            var labels=shell.Navigation.Items.Cast<ListBoxItem>().Select(AutomationProperties.GetName).ToArray();
            Require(labels.SequenceEqual(new[]{"General","Models","Dictation","History","Connections"}),"Connections must be reachable from the main navigation.");
            Require(shell.Navigation.SelectedIndex==(int)MainSection.Dictation,"Manual opening must start on Dictation even though General is first.");
            shell.Navigation.SelectedIndex=(int)MainSection.General;
            var startup=Get<ToggleButton>(window,"startWithWindows");var error=Get<TextBlock>(window,"startupError");
            var service=Get<Expander>(window,"serviceAdvanced");var dictation=Get<Expander>(window,"dictationAdvanced");
            Require(!service.IsExpanded&&!dictation.IsExpanded,"Rare service and transcription recovery controls must start collapsed.");
            Require(startup.IsChecked==false&&startup.IsEnabled&&AutomationProperties.GetName(startup)=="Start with Windows","Startup must be an accessible opt-in switch.");
            var reads=store.Reads;Call(window,"Refresh");Call(window,"Refresh");
            Require(store.Reads==reads&&store.Writes==0&&store.Deletes==0,"Routine controller refresh polls or modifies startup registration.");
            checks.Add("General/Models/Dictation/History/Connections navigation, initial Dictation, opt-in startup and collapsed Advanced without setting changes");

            startup.IsChecked=true;
            Require(startup.IsChecked==true&&store.Value is string command&&command=="\""+Executable+"\" --startup"&&store.Writes==1,"Startup toggle failed to register the explicitly selected executable.");
            startup.IsChecked=false;Require(store.Value==null&&store.Deletes==1,"Startup toggle failed to remove its registration.");
            store.FailWrite=true;startup.IsChecked=true;
            Require(startup.IsChecked==false&&error.Visibility==Visibility.Visible&&error.Text.Length>0&&!error.Text.Contains("private failure detail"),"Startup write failure left a false success or raw exception details.");
            store.FailWrite=false;startup.IsChecked=true;Require(startup.IsChecked==true&&error.Visibility==Visibility.Collapsed,"Explicit startup retry did not recover from a write failure.");
            store.FailDelete=true;startup.IsChecked=false;Require(startup.IsChecked==true&&error.Visibility==Visibility.Visible,"Startup removal failure falsely showed startup disabled.");
            store.FailDelete=false;startup.IsChecked=false;
            var mutations=store.Writes+store.Deletes;
            Call(window,"RefreshStartup");Require(store.Writes+store.Deletes==mutations,"Startup status refresh performed a write.");
            checks.Add("Explicit startup toggle, safe inline write/delete failure, actual-state restoration and retry using only injected registration storage");

            Search(window,shell,"windows-startup","start with windows","General");Require(shell.Navigation.SelectedIndex==(int)MainSection.General,"Startup search missed General.");
            Search(window,shell,"service-address","service address","Connections");Require(service.IsExpanded&&shell.Navigation.SelectedIndex==(int)MainSection.Connections,"Service search failed to expand Connections Advanced.");
            Search(window,shell,"dictionary-hints","recognition hints","Dictation");Require(dictation.IsExpanded&&shell.Navigation.SelectedIndex==(int)MainSection.Dictation,"Dictionary search failed to reveal Dictation Advanced.");
            dictation.IsExpanded=false;Search(window,shell,"compatibility","application compatibility","Dictation");Require(dictation.IsExpanded,"Compatibility search did not reveal its current target.");
            Require(JsonSerializer.Serialize(owner.Settings)==settings&&store.Writes+store.Deletes==mutations,"Search applied settings or startup registration.");
            window.SelectHistory();Require(shell.Navigation.SelectedIndex==(int)MainSection.History,"History command opened the reordered Dictation page.");
            Require(Get<HistoryPanel>(window,"historyPanel")!=null,"History route lost its persistent panel.");
            checks.Add("Search routes General, Connections and Dictation categories, expands Advanced without edits and preserves reordered History command");

            dictation.IsExpanded=false;owner.RetainDictionaryDraft("Synthetic hints","invalid correction","Synthetic unsaved dictionary warning");Call(window,"RefreshAdvancedStatus");
            var summary=Get<TextBlock>(window,"dictationAdvancedSummary");Require(summary.Visibility==Visibility.Visible&&summary.Text.Contains("Synthetic unsaved dictionary warning"),"Collapsing Advanced hid the unsaved dictionary error.");
            owner.ClearDictionaryDraft();Set(window,"compatibilityWarning","Synthetic compatibility save warning");Call(window,"RefreshAdvancedStatus");Require(summary.Visibility==Visibility.Visible&&summary.Text.Contains("Synthetic compatibility save warning"),"Collapsing Advanced hid compatibility save failure.");
            Set(window,"compatibilityWarning","");Call(window,"RefreshAdvancedStatus");checks.Add("Collapsed Dictation Advanced retains visible unsaved dictionary and compatibility failure summaries");

            foreach(var theme in new[]{"Light","Dark"})foreach(var width in new[]{640,1040})
            {
                ThemeManager.Apply(theme);
                foreach(var section in new[]{MainSection.Connections,MainSection.Dictation})foreach(var expanded in new[]{false,true})
                {
                    service.IsExpanded=dictation.IsExpanded=expanded;shell.Navigation.SelectedIndex=(int)section;Layout(shell,width);
                    var page=(ScrollViewer)shell.PageHost.Content;
                    if(expanded)page.ScrollToBottom();else page.ScrollToHome();Layout(shell,width);
                    if(expanded){var open=section==MainSection.Connections?service:dictation;Require(open.ActualHeight>=((FrameworkElement)open.Content).ActualHeight+40&&((FrameworkElement)open.Content).ActualHeight>=48,"Expanded Advanced content has not reached its visible layout state: "+section+", expander="+open.ActualHeight+", content="+((FrameworkElement)open.Content).ActualHeight+", expanded="+open.IsExpanded);}
                    Require(new WindowInteropHelper(window).Handle==0&&PresentationSource.FromVisual(shell)==null,"General rendering attached a native window.");
                    Require(shell.ActualWidth>=width-32&&shell.ActualWidth<=width&&shell.PageHost.ActualWidth>350,"Section layout lost usable compact content width.");
                    var bitmap=new RenderTargetBitmap(width,820,96,96,PixelFormats.Pbgra32);bitmap.Render(shell);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,$"general-navigation-{section}-{(expanded?"advanced":"top")}-{width}-{theme}.png"));png.Save(file);
                }
                checks.Add($"Connections and Dictation top/Advanced layouts at {width} DIP {theme}, no native window or devices");
            }
            Require(JsonSerializer.Serialize(owner.Settings)==settings&&store.Writes+store.Deletes==mutations,"Rendering modified settings or registration.");
            Require(settingsBytes==null?!File.Exists(settingsPath):File.ReadAllBytes(settingsPath).SequenceEqual(settingsBytes),"General startup UI wrote the dictation settings file.");
        }
        finally{owner.ClearDictionaryDraft();window.Close();ThemeManager.Apply(originalTheme);}
    }
}
