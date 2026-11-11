using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace LocalDictation.App.Services;

public static class ThemeManager
{
    public static string Choice {get;private set;}="System";
    public static bool Dark {get;private set;}
    static readonly string[] Keys=["Canvas","Surface","Ink","Muted","Accent","Border","InputBorder","Selected","Warning","OnAccent","Difference","SelectedInk"];
    public static void Apply(string choice)
    {
        Choice=choice;
        var systemDark=false;
        try {using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");systemDark=key?.GetValue("AppsUseLightTheme") is int value&&value==0;}
        catch(System.Security.SecurityException){}
        Dark=choice=="Dark"||choice=="System"&&systemDark;
        var colors=Dark?new[]{"#15191F","#20262F","#F0F3F8","#B4C0D0","#65B8FF","#394451","#8494A8","#283F58","#FFC477","#111827","#51442A","#F0F3F8"}:
            new[]{"#F4F6F8","#FFFFFF","#1F2937","#52606D","#006BB6","#D7DEE7","#6B7785","#E5F2FF","#8A4B00","#FFFFFF","#FFF3CF","#1F2937"};
        for(var i=0;i<Keys.Length;i++)
        {
            var color=(Color)ColorConverter.ConvertFromString(colors[i]);
            if(SystemParameters.HighContrast)color=Keys[i] switch
            {
                "Canvas" or "Surface"=>SystemColors.WindowColor,
                "Accent" or "Selected" or "Difference"=>SystemColors.HighlightColor,
                "OnAccent" or "SelectedInk"=>SystemColors.HighlightTextColor,
                _=>SystemColors.WindowTextColor
            };
            if(System.Windows.Application.Current.Resources[Keys[i]] is SolidColorBrush brush&&!brush.IsFrozen)brush.Color=color;
            else System.Windows.Application.Current.Resources[Keys[i]]=new SolidColorBrush(color);
        }
        foreach(Window window in System.Windows.Application.Current.Windows)TitleBar(window);
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
