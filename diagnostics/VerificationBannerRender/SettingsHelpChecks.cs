using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using LocalDictation.App.Views;
using MaterialDesignThemes.Wpf;

static class SettingsHelpChecks
{
    sealed class OffscreenSource : PresentationSource,IDisposable
    {
        Visual? root;
        bool disposed;
        public OffscreenSource(Visual visual){AddSource();RootVisual=visual;}
        public override Visual RootVisual{get=>root!;set{var old=root;root=value;RootChanged(old,value);}}
        public override bool IsDisposed=>disposed;
        protected override CompositionTarget GetCompositionTargetCore()=>null!;
        public void Dispose(){if(disposed)return;RootVisual=null!;RemoveSource();disposed=true;}
    }
    const BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T found)yield return found;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    static void Layout(FrameworkElement root,int width)
    {
        TransitionAssist.SetDisableTransitions(root,true);
        root.Measure(new Size(width,double.PositiveInfinity));root.Arrange(new Rect(0,0,width,root.DesiredSize.Height));root.UpdateLayout();
        SettleForRender(root);
    }
    // A detached WPF root reports IsVisible=false. SmartHint correctly skips its
    // normal state calculation in that case. This managed source supplies only
    // the visibility lifecycle; it has no CompositionTarget, HWND or input sink.
    public static void SettleForRender(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if(PresentationSource.FromVisual(root)!=null)throw new ArgumentException("Render settling requires a detached visual.",nameof(root));
        TransitionAssist.SetDisableTransitions(root,true);
        using var source=new OffscreenSource(root);
        root.UpdateLayout();
        root.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
    }
    static void Save(FrameworkElement root,int width,string path)
    {
        Layout(root,width);var image=new RenderTargetBitmap(width,(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
        image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using var file=File.Create(path);encoder.Save(file);
    }
    static void MouseUp(UIElement target)=>target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});

    public static void Run(string theme,string output,List<string> checks)
    {
        var assembly=typeof(CapsuleCornerConverter).Assembly;
        var help=assembly.GetType("LocalDictation.App.Views.SettingsHelp");
        Require(help?.IsPublic==true,"Settings help must expose a reusable public helper.");
        var create=help!.GetMethod("Create",Static);
        var content=help.GetMethod("GetPopoverContent",Static);
        Require(create!=null&&content!=null,"Settings help must expose its real popover content for windowless checks.");
        const string label="Local service address";
        const string hint="The local session service supplies installed models and transcriptions. This explanation remains available without changing this setting.";
        var info=(Button)create!.Invoke(null,[label,hint])!;
        Require(info.Focusable&&info.IsTabStop,"Settings info is missing keyboard access.");
        Require(AutomationProperties.GetName(info)=="About "+label&&AutomationProperties.GetHelpText(info)==hint,"Settings info lost its accessible name or full help text.");
        Require(info.ToolTip!=null,"Settings info lost hover help.");
        Layout(info,48);
        Require(info.ActualWidth>=48&&info.ActualHeight>=48,"Settings info lost its desktop interaction target.");
        var glyph=Find<PackIcon>(info).Single();
        Require(glyph.ActualWidth<=20&&glyph.ActualHeight<=20,"Settings info glyph is no longer visually compact.");
        var popover=(FrameworkElement)content!.Invoke(null,[info])!;
        Layout(popover,360);
        Require(Find<TextBlock>(popover).Any(t=>t.Text==label)&&Find<TextBlock>(popover).Any(t=>t.Text==hint),"Settings popover omits the setting or explanation.");
        Require(!Find<Window>(popover).Any(),"Settings help must not create a dialog.");
        Save(popover,360,Path.Combine(output,"settings-help-popover-"+theme+".png"));
        Require(popover.ActualWidth<=360,"Settings help popover is wider than the desktop content bound.");
        checks.Add("compact accessible settings help content "+theme);

        var ui=assembly.GetType("LocalDictation.App.Views.Ui")!;
        var switchRow=ui.GetMethod("SwitchRow",Static)!;
        var toggle=new ToggleButton{IsChecked=false};
        var row=(Grid)switchRow.Invoke(null,["Verify after recording",toggle,hint])!;
        Layout(row,420);
        var rowInfo=Find<Button>(row).Single();
        Require(AutomationProperties.GetHelpText(toggle)==hint,"The switch itself lost its accessible explanation.");
        Require(!Find<TextBlock>(row).Any(t=>t.Text==hint),"A static switch explanation still occupies a paragraph.");
        MouseUp(rowInfo);
        rowInfo.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Require(toggle.IsChecked==false,"Clicking switch info toggled the underlying setting.");
        MouseUp(Find<TextBlock>(row).Single(t=>t.Text=="Verify after recording"));
        Require(toggle.IsChecked==true,"The ordinary switch-row label no longer toggles its setting.");
        toggle.IsEnabled=false;
        MouseUp(Find<TextBlock>(row).Single(t=>t.Text=="Verify after recording"));
        Require(toggle.IsChecked==true,"A disabled switch can be changed by its label.");
        Require(row.ActualHeight<=48.1,"Removing the hint made a one-line switch row taller.");
        checks.Add("switch info isolation and ordinary label click "+theme);

        var panel=new StackPanel();
        var box=new TextBox{Text="http://127.0.0.1:48125"};
        var combo=new ComboBox();combo.Items.Add("System");combo.SelectedIndex=0;
        var addLabel=ui.GetMethod("Label",Static)!;
        addLabel.Invoke(null,[panel,label,box,hint]);
        addLabel.Invoke(null,[panel,"Color theme",combo,"Follow Windows or select a theme."]);
        var warning=new TextBlock{Text="The local service could not be reached. Check its address and try again."};
        warning.SetResourceReference(TextBlock.ForegroundProperty,"Warning");panel.Children.Add(warning);
        var fixture=new Border{Child=panel,Padding=new(16)};fixture.SetResourceReference(Border.BackgroundProperty,"Canvas");
        Save(fixture,448,Path.Combine(output,"settings-help-fields-compact-"+theme+".png"));
        Require(Find<SmartHint>(panel).Where(h=>h.UseFloating&&h.HintProxy?.IsEmpty()==false).All(h=>h.IsHintInFloatingPosition),"Detached field hints did not reach the toolkit's real floating state.");
        Require(Find<TextBox>(panel).Contains(box)&&Find<ComboBox>(panel).Contains(combo),"Context help replaced a real setting control.");
        Require(HintAssist.GetIsFloating(box)&&HintAssist.GetHint(box)?.ToString()==label&&HintAssist.GetIsFloating(combo),"Context help lost the real floating field labels.");
        Require(AutomationProperties.GetName(box)==label,"The actual setting control lost its accessible label.");
        Require(Find<Button>(panel).Count(b=>AutomationProperties.GetName(b).StartsWith("About "))==2&&!Find<TextBlock>(panel).Any(t=>t.Text==hint),"Static field explanations were not replaced by info buttons.");
        Require(warning.Visibility==Visibility.Visible&&warning.ActualHeight>0,"A runtime warning was hidden by help.");
        Require(box.ActualHeight>=48&&combo.ActualHeight>=48&&panel.ActualHeight<210,"Info buttons added extra label/help rows to floating fields.");
        Save(fixture,760,Path.Combine(output,"settings-help-fields-wide-"+theme+".png"));
        Require(!PresentationSource.CurrentSources.Cast<PresentationSource>().Any(),"Windowless settings help checks created a native presentation source.");
        checks.Add("floating fields, runtime warning and compact/wide help layout "+theme);
    }
}
