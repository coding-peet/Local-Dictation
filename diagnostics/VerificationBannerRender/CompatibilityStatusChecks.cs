using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

public static class CompatibilityStatusChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Get<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Fields)!.GetValue(owner)!;
    static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    public static void Run(string output,List<string> checks)
    {
        var originalTheme=ThemeManager.Choice;
        var originalHome=Environment.GetEnvironmentVariable("LOCAL_DICTATION_HOME");
        Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",Path.Combine(output,"compatibility-home"));
        try
        {
            foreach(var theme in new[]{"Light","Dark"})
            {
                ThemeManager.Apply(theme);
                var owner=ReviewWorkflowChecks.Controller(output,new ReviewDocument());
                typeof(AppController).GetField("noHotkey",Fields)!.SetValue(owner,true);
                typeof(AppController).GetField("dictionaryStore",Fields)!.SetValue(owner,new PersonalDictionaryStore(Path.Combine(output,"compatibility-dictionary-"+theme)));
                owner.Settings.FinalOnlyApplications=["example.exe"];
                var window=(SettingsWindow)typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!.Invoke([owner,false]);
                try
                {
                    var shell=(MainWindowShell)window.Content;
                    Get<Button>(window,"resetCompatibility").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Require(owner.Settings.FinalOnlyApplications.Count==0,"Compatibility reset did not clear remembered apps.");
                    foreach(var width in new[]{1040,640})
                    {
                        shell.Measure(new(width,760));shell.Arrange(new Rect(0,0,width,760));shell.UpdateLayout();
                        var status=Get<TextBlock>(window,"connection");
                        Require(shell.StatusArea.Visibility==Visibility.Collapsed,"Successful compatibility reset is still displayed as a global warning.");
                        Require(status.Text=="Remembered applications cleared. Outlook still inserts after Finish."&&status.IsDescendantOf(shell.StatusBar),"Compatibility confirmation is missing from the bottom status bar.");
                        Require(Equals(status.Foreground,Application.Current.FindResource("Muted"))&&status.ActualHeight>0,"Compatibility confirmation is not visible in the normal status color.");
                        SettingsHelpChecks.SettleForRender(shell);
                        var bitmap=new RenderTargetBitmap(width,760,96,96,PixelFormats.Pbgra32);bitmap.Render(shell);
                        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,$"compatibility-status-{width}-{theme}.png"));png.Save(file);
                    }
                    var settingsPath=Path.Combine(AppPaths.Data,"settings.json");
                    using(var locked=new FileStream(settingsPath,FileMode.Open,FileAccess.Read,FileShare.None))
                    {
                        owner.Settings.FinalOnlyApplications=["example.exe"];
                        Get<Button>(window,"resetCompatibility").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        Require(owner.Settings.FinalOnlyApplications.Contains("example.exe")&&Get<TextBlock>(window,"message").Text.StartsWith("Could not save"),"Failed reset lost remembered apps or its actionable warning.");
                    }
                    Get<Button>(window,"resetCompatibility").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Require(Get<TextBlock>(window,"message").Text=="","Successful retry left the earlier compatibility failure visible.");
                    Get<TextBlock>(window,"message").Text="Unrelated microphone warning";
                    Get<Button>(window,"resetCompatibility").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Require(Get<TextBlock>(window,"message").Text=="Unrelated microphone warning","Compatibility success cleared an unrelated warning.");
                    Require(new WindowInteropHelper(window).Handle==0,"Compatibility status verification opened a native window.");
                    checks.Add("Compatibility success uses normal bottom status; save failures preserve apps and visible warning "+theme);
                }
                finally{window.Close();}
            }
        }
        finally{Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",originalHome);ThemeManager.Apply(originalTheme);}
    }
}
