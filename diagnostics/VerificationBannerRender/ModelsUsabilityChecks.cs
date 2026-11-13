using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class ModelsUsabilityChecks
{
    const BindingFlags Fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {if(root is T found)yield return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    public static void Run(string theme,string output,List<string> checks)
    {
        var controller=(AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
        Set(controller,"<Connected>k__BackingField",true);Set(controller,"<Settings>k__BackingField",new Settings());
        var shared=new ModelInfo{Id="example/shared",Name="Whisper · shared",Installed=true,RuntimeTested=true,LiveSupported=true,RemovalRestriction="Shared model files are kept."};
        var owned=new ModelInfo{Id="example/managed",Name="Whisper · installed",Installed=true,RuntimeTested=true,CanRemove=true};
        var window=(SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));Set(window,"controller",controller);
        Set(window,"catalogChoice",new StackPanel());
        Set(window,"catalogModels",new ListBox{DisplayMemberPath="Label",SelectedValuePath="Id",MinHeight=96,MaxHeight=160});
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,
            SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,HeadersVisibility=DataGridHeadersVisibility.Column,MinHeight=110,MaxHeight=230,RowHeight=48,Margin=new(0,8,0,8)};
        Set(window,"installedModels",grid);Set(window,"repository",new TextBox());
        foreach(var name in new[]{"installedEmpty","removalReason","modelActionStatus","importStatus"})Set(window,name,new TextBlock{Text="",FontSize=13,TextWrapping=TextWrapping.Wrap});
        Set(window,"progress",new ProgressBar{Minimum=0,Maximum=100,Height=6});
        var page=(FrameworkElement)typeof(SettingsWindow).GetMethod("ModelsPage",Fields)!.Invoke(window,null)!;
        page.SetResourceReference(Control.BackgroundProperty,"Canvas");
        var catalog=(ListBox)typeof(SettingsWindow).GetField("catalogModels",Fields)!.GetValue(window)!;
        catalog.ItemsSource=new[]{new ModelInfo{Id="example/new-model",Name="Whisper · download",CanInstall=true}};catalog.SelectedIndex=0;
        var rowType=typeof(SettingsWindow).GetNestedType("InstalledModelRow",BindingFlags.NonPublic)!;
        var rows=new[]{Activator.CreateInstance(rowType,[shared])!,Activator.CreateInstance(rowType,[owned])!};grid.ItemsSource=rows;grid.SelectedIndex=0;
        Layout(page,760,720);
        var buttons=Find<Button>(page).ToList();var remove=buttons.Single(b=>b.Content?.ToString()=="Remove selected model");
        Require(!remove.IsEnabled,"Shared model can be removed from Models.");
        Require(Find<TextBlock>(page).Any(t=>t.Text==shared.RemovalRestriction),"Shared model restriction is hidden.");
        grid.SelectedIndex=1;
        var selected=(ModelInfo)typeof(SettingsWindow).GetMethod("SelectedInstalledModel",Fields)!.Invoke(window,null)!;
        Require(selected.Id==owned.Id&&remove.IsEnabled,"Installed-table actions do not use its selected managed model.");
        Require(!Find<TextBlock>(page).Any(t=>t.Text.Contains("Runtime-tested and quality-checked")||t.Text=="Original models"),"Verbose model introduction returned.");
        var test=buttons.Single(b=>b.Content?.ToString()=="Test selected model with audio…");
        Require(test.TransformToAncestor(page).Transform(new Point()).Y>=grid.TransformToAncestor(page).Transform(new Point(0,grid.ActualHeight)).Y,"Model test is above the installed table.");
        Save(page,760,720,Path.Combine(output,"models-simple-"+theme+".png"));checks.Add("plain installed/add model blocks "+theme);
        Layout(page,448,640);Save(page,448,640,Path.Combine(output,"models-simple-compact-"+theme+".png"));checks.Add("selected model management and compact layout "+theme);
        var nav=new ListBox();foreach(var label in new[]{"Dictation","Models","History","Settings"})nav.Items.Add(label);Set(window,"navigation",nav);window.SelectHistory();
        Require(nav.SelectedItem?.ToString()=="History"&&typeof(SettingsWindow).GetMethod("ReviewPage",Fields)==null,"History navigation still depends on the removed Pending Review page.");checks.Add("single History destination "+theme);
    }
    static void Layout(FrameworkElement root,int width,int height){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
    static void Save(FrameworkElement root,int width,int height,string path){var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);png.Save(file);}
}
