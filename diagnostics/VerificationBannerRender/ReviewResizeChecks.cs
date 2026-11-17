using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App.Views;
using LocalDictation.App.Services;
using LocalDictation.Core;

public static class ReviewResizeChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Layout(FrameworkElement root,int width,int height)
    {root.Measure(new(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);}
    static void Save(FrameworkElement root,int width,int height,string path)
    {
        var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        var canvas=new DrawingVisual();using(var draw=canvas.RenderOpen())draw.DrawRectangle((Brush)Application.Current.FindResource("Canvas"),null,new Rect(0,0,width,height));
        image.Render(canvas);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);png.Save(file);
    }
    static void CheckPicker(string theme,bool isPrivate,string output,List<string> checks)
    {
        var doc=new ReviewDocument{PrimaryModel="first",Language="en",IsPrivate=isPrivate,RequiresExplicitInsert=true};doc.SetPrimary("Synthetic saved wording.");
        var controller=ReviewWorkflowChecks.Controller(output,doc);Set(controller,"bannerProgress",new CompactReviewProgress(doc));
        var window=new CompactReviewWindow(controller);
        try
        {
            var root=(Grid)window.Content;var picker=Get<RetranscriptionPicker>(window,"transcription");picker.Toggle();
            Get<TextBlock>(picker,"message").Text="Synthetic validation error. Choose an available model or cancel and keep your current text.";
            // 420 DIP outer height leaves less client space after window chrome.
            Layout(root,400,380);
            var run=Get<Button>(picker,"run");
            ScrollViewer? viewport=null;
            for(DependencyObject? node=run;node!=null;node=VisualTreeHelper.GetParent(node))
                if(node is ScrollViewer scroll){viewport=scroll;break;}
            if(viewport?.Content is UIElement content)
            {viewport.ScrollToVerticalOffset(run.TranslatePoint(new Point(),content).Y);Layout(root,400,380);}
            var bounds=run.TransformToAncestor(root).TransformBounds(new Rect(new Size(run.ActualWidth,run.ActualHeight)));
            var top=root.RowDefinitions[0].ActualHeight;var bottom=top+root.RowDefinitions[1].ActualHeight;
            var measured=new{theme,isPrivate,rootHeight=root.ActualHeight,bodyTop=top,bodyBottom=bottom,runTop=bounds.Top,runBottom=bounds.Bottom,scrollable=viewport!=null};
            File.WriteAllText(Path.Combine(output,"review-picker-measurements-"+theme+"-"+isPrivate+".json"),JsonSerializer.Serialize(measured,new JsonSerializerOptions{WriteIndented=true}));
            Save(root,400,380,Path.Combine(output,"review-resize-picker-"+theme+"-"+isPrivate+".png"));
            Require(bounds.Top>=top-1&&bounds.Bottom<=bottom+1,
                $"Small Review hides picker actions ({theme}, private={isPrivate}): body {top:0}-{bottom:0}, Transcribe {bounds.Top:0}-{bounds.Bottom:0}.");
            Require(doc.InsertedAt==null&&new WindowInteropHelper(window).Handle==0,"Picker layout inserted text or created a native window.");
            checks.Add("minimum-client Review picker actions reachable, private="+isPrivate+" "+theme);
        }
        finally{window.Close();}
    }

    public static void Run(string output,List<string> checks)
    {
        var original=ThemeManager.Choice;var measurements=new List<object>();
        try
        {
            foreach(var theme in new[]{"Light","Dark"})
            {
                ThemeManager.Apply(theme);
                var text=string.Join(" ",Enumerable.Repeat("Synthetic wording stays editable while the transcript area grows with the Review window.",70));
                var doc=new ReviewDocument{PrimaryModel="first",Language="en",RequiresExplicitInsert=true};doc.SetPrimary(text);
                var controller=ReviewWorkflowChecks.Controller(output,doc);
                Set(controller,"<History>k__BackingField",new List<HistoryEntry>());
                Set(controller,"bannerProgress",new CompactReviewProgress(doc));
                var window=new CompactReviewWindow(controller);
                try
                {
                    var root=(FrameworkElement)window.Content;var panel=Get<CompactReviewPanel>(window,"panel");
                    var reader=Get<ScrollViewer>(panel,"transcriptScroll");
                    Layout(root,(int)window.Width,(int)window.Height);
                    Save(root,(int)window.Width,(int)window.Height,Path.Combine(output,"review-resize-default-"+theme+".png"));
                    Layout(root,560,520);var shortReader=reader.ViewportHeight;
                    Save(root,560,520,Path.Combine(output,"review-resize-short-"+theme+".png"));
                    Layout(root,560,900);var tallReader=reader.ViewportHeight;
                    Save(root,560,900,Path.Combine(output,"review-resize-tall-"+theme+".png"));
                    var begin=typeof(CompactReviewPanel).GetMethod("BeginTextEdit",Fields)!;
                    Require((bool)begin.Invoke(panel,[(int?)0])!,"Could not enter editable Review.");
                    var editor=Get<TextBox>(panel,"directEditor");
                    Layout(root,560,520);var shortEditor=editor.ActualHeight;
                    Layout(root,560,900);var tallEditor=editor.ActualHeight;
                    Save(root,560,900,Path.Combine(output,"review-resize-editor-"+theme+".png"));
                    measurements.Add(new{theme,shortReader,tallReader,shortEditor,tallEditor});
                    File.WriteAllText(Path.Combine(output,"review-resize-measurements.json"),JsonSerializer.Serialize(measurements,new JsonSerializerOptions{WriteIndented=true}));
                    Require(tallReader>shortReader+240&&tallEditor>shortEditor+240,
                        $"Review text does not gain height ({theme}): reader {shortReader:0}->{tallReader:0}, editor {shortEditor:0}->{tallEditor:0}.");
                    checks.Add("Review reader/editor gain usable height "+theme);
                    Layout(root,400,420);
                    var insert=Get<Button>(window,"insert");
                    var bounds=insert.TransformToAncestor(root).TransformBounds(new Rect(new Size(insert.ActualWidth,insert.ActualHeight)));
                    Require(editor.ActualHeight>25&&bounds.Top>=0&&bounds.Bottom<=root.ActualHeight+1,
                        "Small Review clipped its text viewport or main Insert action.");
                    checks.Add("small Review retains text and main actions "+theme);
                    Require(editor.Text==text&&doc.OutputText==text&&doc.InsertedAt==null,"Resizing changed text or inserted it.");
                    Require(new WindowInteropHelper(window).Handle==0,"Detached resize check created a native window.");
                    checks.Add("Review resize preserves text and creates no native window "+theme);
                }
                finally{window.Close();}
                CheckPicker(theme,false,output,checks);
                CheckPicker(theme,true,output,checks);
            }
        }
        finally{ThemeManager.Apply(original);}
    }
}
