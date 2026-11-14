using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.App.Services;
using LocalDictation.Core;

static class SettingsContextHelpChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {if(root is T item)yield return item;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    public static void Run(string output,List<string> checks)
    {
        foreach(var theme in new[]{"Light","Dark"})
        {
            ThemeManager.Apply(theme);
            var owner=ReviewWorkflowChecks.Controller(output,new ReviewDocument());
            var models=owner.Models.Select(m=>m with {LiveSupported=true,LiveReady=false}).ToList();Set(owner,"<Models>k__BackingField",models);
            var ctor=typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!;
            var window=(SettingsWindow)ctor.Invoke([owner,false]);
            try
            {
                var root=(FrameworkElement)window.Content;
                root.Measure(new Size(1040,760));root.Arrange(new Rect(0,0,1040,760));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);
                var about=Find<Button>(root).SingleOrDefault(b=>AutomationProperties.GetName(b)=="About Preferred Auto languages");
                Require(about!=null,"Preferred Auto languages has no local explanation icon.");
                var help=AutomationProperties.GetHelpText(about!);
                Require(help.Contains("Auto")&&help.Contains("active model")&&help.Contains("several seconds"),"The local language help omits Auto behavior or live preview latency.");
                var qualifier=Find<TextBlock>(root).SingleOrDefault(t=>t.Text=="(supported by installed models)");
                var label=Find<TextBlock>(root).Single(t=>t.Text=="Preferred Auto languages");
                Require(qualifier!=null&&qualifier.FontSize<label.FontSize,"Installed-model qualifier is not smaller supporting text.");
                Require(qualifier!.Foreground is SolidColorBrush muted&&((SolidColorBrush)Application.Current.FindResource("Muted")).Color==muted.Color,"Installed-model qualifier is not muted.");
                Require(Get<TextBlock>(window,"modelStatus").Text!="Live preview latency may be several seconds.","Latency guidance still occupies a permanent paragraph.");
                var record=Get<Button>(window,"record");Require(record.Content?.ToString()=="Record"&&record.ToolTip?.ToString()?.Contains("Review")==true,"The main Record action retains its technical banner label or lacks its Review explanation.");
                var run=RuntimeHelpers.GetUninitializedObject(typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!);Set(run,"Recording",true);Set(owner,"active",run);
                typeof(SettingsWindow).GetMethod("Refresh",Fields,null,Type.EmptyTypes,null)!.Invoke(window,null);
                Require(record.Content?.ToString()=="Stop","The global action does not reflect active recording.");Set(owner,"active",null);
                typeof(SettingsWindow).GetMethod("Refresh",Fields,null,Type.EmptyTypes,null)!.Invoke(window,null);
                foreach(var width in new[]{640,1040})
                {
                    root.Measure(new Size(width,760));root.Arrange(new Rect(0,0,width,760));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);
                    var image=new RenderTargetBitmap(width,760,96,96,PixelFormats.Pbgra32);image.Render(root);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,$"dictation-help-{theme}-{width}.png"));encoder.Save(file);
                }
                Require(new System.Windows.Interop.WindowInteropHelper(window).Handle==0,"Context help checks created a native window.");
                checks.Add("local Auto/language/latency help, quiet qualifier and Record/Stop copy "+theme);
            }
            finally{Set(owner,"active",null);window.Close();}
        }
    }
}
