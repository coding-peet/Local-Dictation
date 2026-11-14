using System.Reflection;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        // Detached page fixtures inherit the same typography as the real Window.
        page.SetResourceReference(TextElement.FontFamilyProperty,"MaterialDesignFont");
        page.SetValue(TextElement.FontSizeProperty,14d);
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
        var failures=new List<string>();
        void Verify(string name,Action assertion)
        {
            try{assertion();checks.Add(name+" "+theme);}
            catch(Exception error){failures.Add(error.Message);}
        }
        var test=buttons.Single(b=>b.Content?.ToString()=="Test selected model with audio…");
        Require(test.TransformToAncestor(page).Transform(new Point()).Y>=grid.TransformToAncestor(page).Transform(new Point(0,grid.ActualHeight)).Y,"Model test is above the installed table.");
        Verify("Live model status fits its fixed-width cell at wide size",()=>VerifyLiveStatus(grid));
        Verify("download progress is hidden idle, visible active and hidden on completion without losing status",()=>
        {
            var progress=(ProgressBar)typeof(SettingsWindow).GetField("progress",Fields)!.GetValue(window)!;
            var status=(TextBlock)typeof(SettingsWindow).GetField("importStatus",Fields)!.GetValue(window)!;
            var update=typeof(SettingsWindow).GetMethod("UpdateModelActions",Fields)!;
            const string error="Synthetic download failed. Check the URL and retry.";
            var stateErrors=new List<string>();status.Text=error;
            try
            {
                foreach(var state in new (string Name,string? Job,Visibility Expected)[]{("idle",null,Visibility.Collapsed),("active","synthetic-download",Visibility.Visible),("finished",null,Visibility.Collapsed)})
                {
                    Set(window,"job",state.Job);update.Invoke(window,null);
                    if(progress.Visibility!=state.Expected)stateErrors.Add(state.Name+" progress is "+progress.Visibility+", expected "+state.Expected);
                    Require(status.Text==error&&status.Visibility==Visibility.Visible,"Progress visibility hid or replaced actionable model status.");
                }
                Require(stateErrors.Count==0,string.Join("; ",stateErrors));
            }
            finally{Set(window,"job",null);update.Invoke(window,null);status.Text="";}
        });
        Layout(page,760,720);Save(page,760,720,Path.Combine(output,"models-simple-"+theme+".png"));checks.Add("plain installed/add model blocks "+theme);
        Layout(page,448,640);Verify("Live model status fits without compact horizontal overflow",()=>VerifyLiveStatus(grid));
        Save(page,448,640,Path.Combine(output,"models-simple-compact-"+theme+".png"));checks.Add("selected model management and compact layout "+theme);
        var nav=new ListBox();foreach(var label in new[]{"Dictation","Models","History","Settings"})nav.Items.Add(label);Set(window,"navigation",nav);window.SelectHistory();
        Require(nav.SelectedItem?.ToString()=="History"&&typeof(SettingsWindow).GetMethod("ReviewPage",Fields)==null,"History navigation still depends on the removed Pending Review page.");checks.Add("single History destination "+theme);
        Require(failures.Count==0,string.Join(Environment.NewLine,failures));
    }
    static void VerifyLiveStatus(DataGrid grid)
    {
        var text=Find<TextBlock>(grid).Single(block=>block.Text=="After recording");
        DependencyObject? parent=text;while(parent!=null&&parent is not DataGridCell)parent=VisualTreeHelper.GetParent(parent);
        Require(parent is DataGridCell,"The Live status is not hosted by its real table cell.");
        var cell=(DataGridCell)parent!;
        var required=new FormattedText(text.Text,CultureInfo.CurrentUICulture,text.FlowDirection,
            new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,text.Foreground,
            VisualTreeHelper.GetDpi(text).PixelsPerDip){MaxTextWidth=text.ActualWidth};
        var bounds=text.TransformToAncestor(cell).TransformBounds(new Rect(text.RenderSize));
        var last=text.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        var ink=required.BuildGeometry(new Point()).Bounds;
        if(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(text) is {} clip)
        {
            var visible=clip.Bounds;visible.Inflate(0.01,0.01);
            Require(visible.Contains(ink),$"Live status glyphs are clipped: ink={ink}, visible={clip.Bounds}.");
        }
        Require(last.Right<=text.ActualWidth+0.5&&last.Bottom<=text.ActualHeight+0.5,"The last Live-status character is clipped by its text layout.");
        Require(text.ActualHeight+0.01>=required.Height&&bounds.Left>=cell.Padding.Left-0.01&&bounds.Right<=cell.ActualWidth-cell.Padding.Right+0.01&&bounds.Top>=-0.01&&bounds.Bottom<=cell.ActualHeight+0.01,
            $"Live status is clipped: available text={text.ActualWidth:0.0}x{text.ActualHeight:0.0}, required height={required.Height:0.0}, cell={cell.ActualWidth:0.0}x{cell.ActualHeight:0.0}, bounds={bounds}.");
        Require(cell.ActualHeight<=48.5&&grid.Columns.Sum(column=>column.ActualWidth)<=grid.ActualWidth+1,
            "Making the Live status readable expanded the row or introduced horizontal table overflow.");
    }
    static void Layout(FrameworkElement root,int width,int height){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);}
    static void Save(FrameworkElement root,int width,int height,string path){var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);png.Save(file);}
}
