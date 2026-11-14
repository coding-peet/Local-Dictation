using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class ShortcutSettingsChecks
{
    const BindingFlags Fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static object? Call(object target,string name,params object?[] args)
    {var method=target.GetType().GetMethod(name,Fields);Require(method!=null,"Missing shortcut UI behavior: "+name);return method!.Invoke(target,args);}
    sealed class DetachedPresentationSource : System.Windows.PresentationSource
    {
        public override Visual RootVisual {get;set;}=null!;
        public override bool IsDisposed=>false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore()=>null!;
    }
    static bool Press(object window,Key key,bool down)
    {
        var e=new KeyEventArgs(Keyboard.PrimaryDevice,new DetachedPresentationSource(),0,key)
            {RoutedEvent=down?System.Windows.UIElement.PreviewKeyDownEvent:System.Windows.UIElement.PreviewKeyUpEvent};
        Get<Button>(window,"shortcutValue").RaiseEvent(e);return e.Handled;
    }
    static IEnumerable<T> Find<T>(System.Windows.DependencyObject root) where T:System.Windows.DependencyObject
    {
        if(root is T match)yield return match;
        foreach(var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
            foreach(var found in Find<T>(child))yield return found;
    }
    public static void Run(string output,List<string> checks)
    {
        var owner=ReviewWorkflowChecks.Controller(output,new ReviewDocument());
        owner.Models.Add(new ModelInfo{Id="catalog-only",Name="Not installed",Installed=false,Languages=["en"]});
        var input=Get<GlobalInput>(owner,"input");
        var ctor=typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!;
        var window=(SettingsWindow)ctor.Invoke([owner,false]);
        try
        {
            Get<ListBox>(window,"navigation").SelectedIndex=3;
            Require(Get<ComboBox>(window,"primary").Items.Cast<ModelInfo>().All(m=>m.Installed),"Dictation offers uninstalled models that cannot be saved.");
            var field=Get<System.Windows.FrameworkElement>(window,"shortcutValue");
            Require(field is Button,"The shortcut setting is still presented as a text field instead of an explicit keycap recorder.");
            var button=(Button)field;
            Require(Equals(button.Style,System.Windows.Application.Current.FindResource("Md3IconButton")),"Shortcut editing still looks like a stretched input field.");
            var pressMode=Get<RadioButton>(window,"shortcutPress");var holdMode=Get<RadioButton>(window,"shortcutHold");
            holdMode.IsChecked=true;Require(owner.Settings.ShortcutMode==ShortcutMode.Hold&&Settings.Load().ShortcutMode==ShortcutMode.Hold,"Hold mode did not save automatically.");
            pressMode.IsChecked=true;Require(owner.Settings.ShortcutMode==ShortcutMode.Toggle,"Press mode did not save automatically.");
            var focusAllowed=false;var focusRequests=0;
            typeof(SettingsWindow).GetField("focusShortcutRecorder",Fields)!.SetValue(window,(Func<bool>)(()=>{focusRequests++;return focusAllowed;}));
            IEnumerable<string> Caps()=>Find<System.Windows.Controls.Border>(Get<WrapPanel>(window,"shortcutKeys")).Where(b=>Equals(b.Tag,"ShortcutKey")).Select(b=>((TextBlock)b.Child).Text);
            void Click()=>button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
            Require(Caps().SequenceEqual(new[]{"Ctrl","Shift"}),"The saved shortcut is not visible as individual keycaps.");
            button.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,null,button){RoutedEvent=Keyboard.GotKeyboardFocusEvent});
            Require(!input.CapturingShortcut,"Tab focus implicitly started shortcut capture.");
            Click();Require(!input.CapturingShortcut&&focusRequests==1,"The recorder armed global capture without acquiring keyboard focus.");
            focusAllowed=true;
            Click();Require(input.CapturingShortcut&&Get<TextBlock>(window,"shortcutAction").Text=="Listening…","Click did not activate a visible Listening state.");
            Press(window,Key.LeftCtrl,true);Require(Caps().SequenceEqual(new[]{"Ctrl"}),"Pressed Ctrl did not appear as a keycap.");
            Press(window,Key.LeftAlt,true);Press(window,Key.Space,true);
            Require(Caps().SequenceEqual(new[]{"Ctrl","Alt","Space"}),"Live pressed keys are not shown as individual keycaps.");
            Press(window,Key.LeftCtrl,false);Press(window,Key.LeftAlt,false);Press(window,Key.Space,false);
            Require(owner.Settings.Shortcut=="Ctrl+Alt+Space"&&Settings.Load().Shortcut=="Ctrl+Alt+Space"&&input.Shortcut.ToString()=="Ctrl+Alt+Space",
                "Pressing a complete chord did not save and apply it.");
            Require(!input.CapturingShortcut&&Get<TextBlock>(window,"shortcutAction").Text=="","A successful save did not end capture and restore Change.");
            Click();Press(window,Key.LeftCtrl,true);Press(window,Key.LeftCtrl,false);
            Require(owner.Settings.Shortcut=="Ctrl+Alt+Space"&&Get<TextBlock>(window,"saveStatus").Text.StartsWith("Not saved:"),
                "An invalid gesture replaced the last valid shortcut or hid its error.");
            Press(window,Key.Escape,true);
            Require(!input.CapturingShortcut&&Caps().SequenceEqual(new[]{"Ctrl","Alt","Space"})&&Get<TextBlock>(window,"saveStatus").Text=="Saved automatically",
                "Escape did not restore the saved keycaps and resume commands.");
            Click();Press(window,Key.LeftAlt,true);Press(window,Key.F9,true);Call(window,"EndShortcutCapture");
            Require(!input.CapturingShortcut&&owner.Settings.Shortcut=="Ctrl+Alt+Space","Leaving capture committed a partial gesture.");
            Click();Press(window,Key.LeftCtrl,true);Press(window,Key.LeftShift,true);Press(window,Key.LeftShift,false);Press(window,Key.LeftCtrl,false);
            Require(owner.Settings.Shortcut=="Ctrl+Shift"&&!input.CapturingShortcut,"Default modifier-only capture did not save and return to idle.");
            Click();Require(!Press(window,Key.Tab,true)&&!input.CapturingShortcut,"Tab was consumed or left capture active.");
            Click();Press(window,Key.LeftCtrl,true);Require(Press(window,Key.F,true),"The recorder did not handle the routed F key.");
            Press(window,Key.F,false);Press(window,Key.LeftCtrl,false);
            Require(owner.Settings.Shortcut=="Ctrl+F"&&!input.CapturingShortcut,"Search intercepted the shortcut gesture or capture stayed active.");
            Click();Click();Require(!input.CapturingShortcut,"Clicking the active recorder did not cancel capture.");
            var originalHome=Environment.GetEnvironmentVariable("LOCAL_DICTATION_HOME");
            var blockedHome=Path.Combine(output,"shortcut-save-blocked");File.WriteAllText(blockedHome,"synthetic fixture");
            try
            {
                Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",blockedHome);
                Click();Press(window,Key.LeftAlt,true);Press(window,Key.F8,true);Press(window,Key.F8,false);Press(window,Key.LeftAlt,false);
                Require(owner.Settings.Shortcut=="Ctrl+F"&&input.CapturingShortcut&&Get<TextBlock>(window,"saveStatus").Text.StartsWith("Not saved:"),
                    "A failed save changed the active shortcut or reported success.");
                Press(window,Key.Escape,true);Require(!input.CapturingShortcut&&Caps().SequenceEqual(new[]{"Ctrl","F"}),"Cancelling a failed save did not restore the previous keycaps.");
            }
            finally{Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",originalHome);}
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            try
            {
                var endpoint=new Uri("http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port);
                var launches=0;
                var result=Task.Run(()=>new LocalServiceStartup().EnsureStarted(endpoint,()=>LocalApiLauncher.IsListening(endpoint),()=>{launches++;return Task.CompletedTask;})).GetAwaiter().GetResult();
                Require(result==LocalServiceStartResult.AlreadyListening&&launches==0,"Real loopback probe attempted to replace an existing listener.");
            }
            finally{listener.Stop();}
            Require(new WindowInteropHelper(window).Handle==0,"Shortcut settings checks created a native window.");
            checks.Add("routed shortcut persistence, command isolation, invalid/Escape/Tab/focus-loss handling and existing-listener probe without native input");
            Get<ListBox>(window,"navigation").SelectedIndex=3;
            var root=(System.Windows.FrameworkElement)window.Content;
            void SaveState(string theme,string state)
            {
                ThemeManager.Apply(theme);
                foreach(var width in new[]{640,1040})
                {
                    root.Measure(new(width,760));root.Arrange(new(0,0,width,760));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);
                    var bindingRow=(System.Windows.FrameworkElement)System.Windows.LogicalTreeHelper.GetParent(Get<WrapPanel>(window,"shortcutKeys"));
                    foreach(var cap in Find<System.Windows.Controls.Border>(Get<WrapPanel>(window,"shortcutKeys")).Where(b=>Equals(b.Tag,"ShortcutKey")))
                    {
                        var origin=cap.TransformToAncestor(bindingRow).Transform(new System.Windows.Point());
                        Require(origin.X>=0&&origin.Y>=0&&origin.X+cap.ActualWidth<=bindingRow.ActualWidth+0.1&&origin.Y+cap.ActualHeight<=bindingRow.ActualHeight+0.1,
                            "A shortcut keycap extends outside the recorder.");
                    }
                    var bitmap=new RenderTargetBitmap(width,760,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file=File.Create(Path.Combine(output,$"shortcut-{state}-{theme}-{width}.png"));encoder.Save(file);
                }
            }
            // Recover from the synthetic save failure before checking visible saved states.
            Click();Press(window,Key.LeftCtrl,true);Press(window,Key.LeftShift,true);Press(window,Key.LeftShift,false);Press(window,Key.LeftCtrl,false);
            foreach(var theme in new[]{"Light","Dark"})
            {
                SaveState(theme,"idle");Click();SaveState(theme,"listening");
                Press(window,Key.LeftCtrl,true);Press(window,Key.LeftShift,true);Press(window,Key.LeftAlt,true);Press(window,Key.F24,true);
                SaveState(theme,"preview");
                Press(window,Key.F24,false);Press(window,Key.LeftAlt,false);Press(window,Key.LeftShift,false);Press(window,Key.LeftCtrl,false);
                Require(!input.CapturingShortcut&&Get<TextBlock>(window,"saveStatus").Text=="Saved automatically","Saving did not recover after the synthetic failure.");
                SaveState(theme,"saved");
            }
            checks.Add("explicit keycap recorder states, focus acquisition, failed-save preservation and narrow/wide Light/Dark renders");
        }
        finally{window.Close();}
    }
}
