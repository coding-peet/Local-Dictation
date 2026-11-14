using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class DeleteDialogChecks
{
    const BindingFlags Instance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    sealed class DetachedPresentationSource : PresentationSource
    {
        public override Visual RootVisual {get;set;}=null!;
        public override bool IsDisposed=>false;
        protected override CompositionTarget GetCompositionTargetCore()=>null!;
    }
    static void Require([DoesNotReturnIf(false)] bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Logical<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T found)yield return found;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var descendant in Logical<T>(child))yield return descendant;
    }
    static IEnumerable<T> Visual<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T found)yield return found;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Visual<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    static Button Button(Window window,string name)=>Logical<Button>(window).Single(button=>AutomationProperties.GetName(button)==name);
    static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    static KeyEventArgs Key(UIElement target,System.Windows.Input.Key key,bool system=false)
    {
        var args=new KeyEventArgs(Keyboard.PrimaryDevice,new DetachedPresentationSource(),0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent};
        if(system)
        {
            // Construct only the managed routed event; never send Windows input.
            var mark=typeof(KeyEventArgs).GetMethod("MarkSystem",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(mark!=null,"The fixture cannot represent an Alt+F4 routed event on this WPF version.");mark!.Invoke(args,null);
        }
        target.RaiseEvent(args);return args;
    }
    static DictationItem Item()
    {
        var document=new ReviewDocument{Created=new DateTimeOffset(2026,11,14,14,37,0,TimeSpan.Zero)};
        document.SetPrimary("PRIVATE TRANSCRIPT MUST NOT APPEAR");document.ManualFinalDraft="PRIVATE EDIT MUST NOT APPEAR";
        return new(document,3,true,true,false,"PRIVATE PREVIEW MUST NOT APPEAR");
    }
    public static void Run(string output,List<string> checks)
    {
        var type=typeof(HistoryPanel).Assembly.GetType("LocalDictation.App.Views.DeleteDictationDialog");
        Require(type!=null&&typeof(Window).IsAssignableFrom(type),"History deletion has no themed confirmation window.");
        var confirm=type!.GetMethod("Confirm",BindingFlags.Public|BindingFlags.Static,null,[typeof(Window),typeof(DictationItem)],null);
        Require(confirm?.ReturnType==typeof(bool),"The themed delete confirmation lacks its scoped public API.");
        Require(type.GetConstructor([typeof(DictationItem)])!=null,"The dialog cannot be constructed with a synthetic dictation.");
        var decision=type.GetProperty("Confirmed",Instance);
        Require(decision?.PropertyType==typeof(bool),"The dialog has no reviewable confirmation decision independent of a native modal loop.");
        Window Create(DictationItem item)=>(Window)Activator.CreateInstance(type,item)!;
        foreach(var action in new[]{"Cancel","Escape","Enter","AltF4","Close","Delete","DeleteEnter"})
        {
            var item=Item();var before=JsonSerializer.Serialize(item);var window=Create(item);var closed=false;
            window.Closed+=(_,_)=>closed=true;
            try
            {
                var cancel=Button(window,"Cancel");var delete=Button(window,"Delete");
                Require(cancel.IsDefault&&cancel.IsCancel&&!delete.IsDefault&&!delete.IsCancel,"Deletion is the default or Cancel lacks safe keyboard behavior.");
                Require(ReferenceEquals(FocusManager.GetFocusedElement(window),cancel),"The safe Cancel action does not receive initial logical focus.");
                var copy=string.Join(" ",Logical<TextBlock>(window).Select(block=>block.Text));
                Require(copy.Contains(item.Document.Created.LocalDateTime.ToString("g",CultureInfo.CurrentCulture))&&!copy.Contains("PRIVATE",StringComparison.Ordinal),"The confirmation loses timestamp identity or exposes transcript/preview content.");
                Require(copy.Contains("recording",StringComparison.OrdinalIgnoreCase)&&copy.Contains("transcript",StringComparison.OrdinalIgnoreCase)&&copy.Contains("review",StringComparison.OrdinalIgnoreCase)&&copy.Contains("cannot be undone",StringComparison.OrdinalIgnoreCase),"Deletion scope or irreversible meaning is unclear.");
                Require(window.WindowStyle==WindowStyle.None&&window.ResizeMode==ResizeMode.NoResize&&!window.ShowInTaskbar&&window.AllowsTransparency&&window.WindowStartupLocation==WindowStartupLocation.CenterOwner,"The delete dialog retains native question-window chrome or is not owner-centered.");
                Require(new WindowInteropHelper(window).Handle==0,"Constructing the dialog created a native window.");
                switch(action)
                {
                    case "Cancel":Click(cancel);break;
                    case "Escape":Require(Key(window,System.Windows.Input.Key.Escape).Handled,"Escape did not cancel the dialog.");break;
                    case "Enter":Require(Key(window,System.Windows.Input.Key.Enter).Handled,"Unfocused Enter did not use safe cancellation.");break;
                    case "AltF4":Require(Key(window,System.Windows.Input.Key.F4,true).Handled,"Alt+F4 did not cancel the dialog.");break;
                    case "Close":window.Close();break;
                    case "Delete":Click(delete);break;
                    case "DeleteEnter":FocusManager.SetFocusedElement(window,delete);Require(Key(delete,System.Windows.Input.Key.Enter).Handled,"Explicit Enter on focused Delete did not finish the decision.");break;
                }
                var expected=action is "Delete" or "DeleteEnter";
                Require(closed&&(bool)decision!.GetValue(window)! == expected,"The "+action+" decision was unsafe or did not close without a native modal loop.");
                Click(expected?cancel:delete);
                Require((bool)decision!.GetValue(window)! == expected,"An already finished confirmation changed its decision.");
                Require(JsonSerializer.Serialize(item)==before,"The confirmation itself modified dictation data.");
                Require(new WindowInteropHelper(window).Handle==0,"Dialog actions created a native window.");
            }
            finally{if(!closed)window.Close();}
        }
        checks.Add("themed delete confirmation safe default, Cancel/Escape/Enter/AltF4/close, explicit Delete, single decision and no data changes");
        var previousTheme=ThemeManager.Choice;
        try
        {
            foreach(var theme in new[]{"Light","Dark"})
            {
                ThemeManager.Apply(theme);var window=Create(Item());
                try
                {
                    var root=(FrameworkElement)window.Content;
                    foreach(var width in new[]{440,320})
                    {
                        root.Measure(new Size(width,double.PositiveInfinity));root.Arrange(new Rect(0,0,width,root.DesiredSize.Height));root.UpdateLayout();
                        SettingsHelpChecks.SettleForRender(root);
                        var surface=Logical<Border>(root).First();
                        Require(surface.CornerRadius.TopLeft>=24&&surface.Background is SolidColorBrush brush&&brush.Color==((SolidColorBrush)Application.Current.FindResource("SurfaceContainerHigh")).Color,"The confirmation lost its rounded tonal Material surface.");
                        var buttons=Visual<Button>(root).Where(button=>AutomationProperties.GetName(button) is "Cancel" or "Delete").ToArray();
                        Require(buttons.Length==2&&buttons.All(button=>button.ActualHeight>=48&&button.ActualWidth>=48),"The confirmation shrank a button interaction target.");
                        Require(buttons.All(button=>button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize)).Right<=width+0.01),"Narrow confirmation clips an action.");
                        Require(Visual<TextBlock>(root).Where(block=>block.Text.Length>0).All(block=>block.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward).Bottom<=block.ActualHeight+0.5),"Narrow confirmation clips its explanation or timestamp.");
                        Require(new WindowInteropHelper(window).Handle==0&&PresentationSource.FromVisual(root)==null,"Dialog rendering created a native window source.");
                        Save(root,width,Path.Combine(output,$"delete-confirmation-{theme}-{width}.png"));
                        checks.Add($"rounded themed delete confirmation content and actions at {width} DIP {theme}");
                    }
                }
                finally{window.Close();}
            }
        }
        finally{ThemeManager.Apply(previousTheme);}
    }
    static void Save(FrameworkElement root,int width,string path)
    {
        var bitmap=new RenderTargetBitmap(width,(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
}