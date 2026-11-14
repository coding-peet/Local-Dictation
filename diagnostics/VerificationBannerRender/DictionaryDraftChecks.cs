using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.Core;
public static class DictionaryDraftChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object x,string n,object? v)=>x.GetType().GetField(n,Fields)!.SetValue(x,v);
    static T Get<T>(object x,string n)=>(T)x.GetType().GetField(n,Fields)!.GetValue(x)!;
    static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    static SettingsWindow Window(AppController owner)=>(SettingsWindow)typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!.Invoke([owner,false]);
    public static void Run(string output,List<string> checks)
    {
        var buildRoot=Path.GetFullPath(@"C:\AI\build\Local-Dictation-personal-tools")+Path.DirectorySeparatorChar;
        var testRoot=Path.GetFullPath(Path.Combine(output,"dictionary-draft-fixture-"+Guid.NewGuid().ToString("N")));
        Require(testRoot.StartsWith(buildRoot,StringComparison.OrdinalIgnoreCase)&&!Directory.Exists(testRoot),"Dictionary test requires a fresh isolated directory below the personal-tools build root.");
        var store=new PersonalDictionaryStore(testRoot);
        var owner=ReviewWorkflowChecks.Controller(output,new ReviewDocument());Set(owner,"noHotkey",true);Set(owner,"dictionaryStore",store);
        var window=Window(owner);
        var navigation=Get<ListBox>(window,"navigation");navigation.SelectedIndex=3;
        var shell=(MainWindowShell)window.Content;shell.Measure(new(1040,820));shell.Arrange(new Rect(0,0,1040,820));shell.UpdateLayout();SettingsHelpChecks.SettleForRender(shell);
        var page=(ScrollViewer)shell.PageHost.Content;var hints=Get<TextBox>(window,"dictionaryHints");
        page.ScrollToVerticalOffset(Math.Max(0,hints.TranslatePoint(new Point(),(UIElement)page.Content).Y-120));shell.UpdateLayout();SettingsHelpChecks.SettleForRender(shell);
        var bitmap=new RenderTargetBitmap(1040,820,96,96,PixelFormats.Pbgra32);bitmap.Render(shell);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"dictionary-editor.png")))png.Save(file);
        Require(new WindowInteropHelper(window).Handle==0,"Dictionary rendering opened a native window.");
        var corrections=Get<TextBox>(window,"dictionaryCorrections");corrections.Text="invalid pair without delimiter";window.Close();
        var reopened=Window(owner);Require(Get<TextBox>(reopened,"dictionaryCorrections").Text==corrections.Text,"Closing Settings discarded an invalid unsaved dictionary draft.");
        Get<TextBox>(reopened,"dictionaryCorrections").Text="heard => written";var runType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;Set(owner,"active",System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(runType));reopened.Close();Set(owner,"active",null);
        var afterBusy=Window(owner);Require(Get<TextBox>(afterBusy,"dictionaryCorrections").Text=="heard => written","Busy saving lost dictionary edits on close.");
        var blockedPath=Path.GetFullPath(Path.Combine(testRoot,"personal-dictionary.settings"));
        Require(blockedPath.StartsWith(testRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&!File.Exists(blockedPath)&&!Directory.Exists(blockedPath),"The synthetic failure placeholder must be a new owned path.");
        Directory.CreateDirectory(blockedPath);Get<TextBox>(afterBusy,"dictionaryHints").Text="unsaved synthetic name";afterBusy.Close();
        var afterStorage=Window(owner);Require(Get<TextBox>(afterStorage,"dictionaryHints").Text=="unsaved synthetic name","Storage failure lost dictionary edits on close.");
        Require(Directory.Exists(blockedPath)&&Directory.GetFileSystemEntries(blockedPath).Length==0,"Synthetic failure directory unexpectedly contains files.");
        Directory.Delete(blockedPath,false);afterStorage.Close();
        Require(store.Load().Hints.Contains("unsaved synthetic name"),"Retained dictionary draft could not be saved after storage recovery.");
        checks.Add("Dictionary invalid/busy/storage-failed drafts survive close/reopen and recover using only an explicitly injected isolated store");
    }
}
