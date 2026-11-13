using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Services;

public static class ThemeManager
{
    public static string Choice {get;private set;}="System";
    public static bool Dark {get;private set;}
    public static event Action? Changed;
    sealed class ColorSource : DependencyObject
    {
        public static readonly DependencyProperty ColorProperty=DependencyProperty.Register(nameof(Color),typeof(Color),typeof(ColorSource));
        public Color Color {get=>(Color)GetValue(ColorProperty);set=>SetValue(ColorProperty,value);}
    }
    static readonly Dictionary<string,ColorSource> semantic=[];
    static bool listening;
    static readonly string[] Roles=["Canvas","Surface","SurfaceContainer","SurfaceContainerHigh","Ink","Muted","Accent","OnAccent","Border","InputBorder","Selected","SelectedInk","Warning","Difference","DifferenceInk","Recording"];
    static readonly string[] Light=["#FEF7FF","#FFFBFE","#F3EDF7","#ECE6F0","#1D1B20","#49454F","#6750A4","#FFFFFF","#CAC4D0","#79747E","#E8DEF8","#1D192B","#8C1D18","#FFD8E4","#31111D","#984061"];
    static readonly string[] Night=["#141218","#1D1B20","#211F26","#2B2930","#E6E0E9","#CAC4D0","#D0BCFF","#381E72","#49454F","#938F99","#4A4458","#E8DEF8","#FFB4AB","#633B48","#FFD8E4","#FFB0C8"];

    public static void Apply(string choice)
    {
        Choice=choice;
        if(!listening)
        {
            listening=true;
            SystemEvents.UserPreferenceChanged+=(_,_)=>RefreshSystemTheme();
            SystemParameters.StaticPropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(SystemParameters.HighContrast))RefreshSystemTheme();};
        }
        Dark=choice=="Dark"||choice=="System"&&Theme.GetSystemTheme()==BaseTheme.Dark;
        var resources=System.Windows.Application.Current.Resources;
        var colors=Dark?Night:Light;
        Color Role(string name)=>SystemParameters.HighContrast?name switch
        {
            "Canvas" or "Surface" or "SurfaceContainer" or "SurfaceContainerHigh"=>SystemColors.WindowColor,
            "Accent" or "Selected" or "Difference"=>SystemColors.HighlightColor,
            "OnAccent" or "SelectedInk" or "DifferenceInk"=>SystemColors.HighlightTextColor,
            _=>SystemColors.WindowTextColor
        }:(Color)ColorConverter.ConvertFromString(colors[Array.IndexOf(Roles,name)]);

        resources.SetTheme(Theme.Create(Dark?BaseTheme.Dark:BaseTheme.Light,Role("Accent"),Role("Accent")));
        // Keep semantic brushes mutable: views holding them update without rebuilding controls or focus.
        for(var i=0;i<Roles.Length;i++)
        {
            var key=Roles[i];
            if(!semantic.TryGetValue(key,out var source))
            {
                semantic[key]=source=new ColorSource();
                var brush=new SolidColorBrush();
                // A binding keeps WPF from freezing a brush retained by a view's phrase inlines.
                BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new Binding(nameof(ColorSource.Color)){Source=source});
                resources[key]=brush;
            }
            source.Color=Role(key);
        }
        foreach(var (key,role) in new (string,string)[]
        {
            ("MaterialDesign.Brush.Background","Canvas"),("MaterialDesign.Brush.Paper","Surface"),
            ("MaterialDesign.Brush.Card.Background","SurfaceContainer"),("MaterialDesign.Brush.Foreground","Ink"),
            ("MaterialDesign.Brush.ForegroundLight","Muted"),("MaterialDesign.Brush.ForegroundBody","Ink"),
            ("MaterialDesign.Brush.Divider","Border"),("MaterialDesign.Brush.Primary","Accent"),
            ("MaterialDesign.Brush.Primary.Foreground","OnAccent"),("MaterialDesign.Brush.Secondary","Accent"),
            ("MaterialDesign.Brush.Secondary.Foreground","OnAccent"),("MaterialDesign.Brush.TextBox.OutlineBorder","InputBorder"),
            ("MaterialDesign.Brush.TextBox.OutlineInactiveBorder","InputBorder"),("MaterialDesign.Brush.ValidationError","Warning"),
            ("MaterialDesign.Brush.ToggleButton.Switch.TrackOffBackground","SurfaceContainerHigh")
        })SetBrush(resources,key,Role(role));
        if(SystemParameters.HighContrast)
        {
            // Toolkit templates use these paired roles for dropdowns, menus and focus states.
            foreach(var key in new[]{"MaterialDesign.Brush.Primary.Light","MaterialDesign.Brush.Primary.Dark","MaterialDesign.Brush.Secondary.Light","MaterialDesign.Brush.Secondary.Dark"})
            {SetBrush(resources,key,Role("Accent"));SetBrush(resources,key+".Foreground",Role("OnAccent"));}
            foreach(var key in new[]{"MaterialDesign.Brush.CheckBoxOff","MaterialDesign.Brush.TextBox.Border","MaterialDesign.Brush.ToggleButton.Switch.TrackOffOutline"})SetBrush(resources,key,Role("InputBorder"));
        }
        foreach(Window window in System.Windows.Application.Current.Windows)TitleBar(window);
        Changed?.Invoke();
    }
    static void RefreshSystemTheme()
    {
        var app=System.Windows.Application.Current;
        if(app!=null&&!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke(()=>Apply(Choice));
    }
    static void SetBrush(ResourceDictionary resources,string key,Color color)
    {
        if(resources[key] is SolidColorBrush brush&&!brush.IsFrozen)
        {brush.BeginAnimation(SolidColorBrush.ColorProperty,null);brush.Color=color;}
        else resources[key]=new SolidColorBrush(color);
    }
    public static void TitleBar(Window window)
    {
        if(window.Style==null)window.SetResourceReference(FrameworkElement.StyleProperty,typeof(Window));
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return;
        var value=Dark&&!SystemParameters.HighContrast?1:0;
        DwmSetWindowAttribute(handle,20,ref value,sizeof(int));
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
}
