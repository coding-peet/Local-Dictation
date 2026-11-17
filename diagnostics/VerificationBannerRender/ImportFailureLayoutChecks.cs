using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

public static class ImportFailureLayoutChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
    {if(root is T found)yield return found;for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,index)))yield return child;}
    public static void Run(string output,List<string> checks)
    {
        var originalTheme=ThemeManager.Choice;
        try
        {
            foreach(var theme in new[]{"Light","Dark"})
            {
                ThemeManager.Apply(theme);
                const string hidden="SENSITIVE-IMPORT-DETAIL-MARKER";
                var queue=new AudioImportQueue(["missing-example.wav","partial-example.wav"]);
                queue.RunAsync((path,token)=>path.StartsWith("missing")?Task.FromException<PreparedAudioImport>(new FileNotFoundException(hidden)):Task.FromResult(new PreparedAudioImport(path,"synthetic",new byte[32])),
                    (audio,document,token)=>{document.SetPrimary("Synthetic received partial text.");throw new HttpRequestException(hidden);},
                    (audio,document)=>throw new IOException(hidden),CancellationToken.None).GetAwaiter().GetResult();
                var document=new ReviewDocument();document.SetPrimary("Synthetic import failure inspection.");
                var owner=ReviewWorkflowChecks.Controller(output,document);
                typeof(AppController).GetField("audioImportQueue",Fields)!.SetValue(owner,queue);
                typeof(AppController).GetField("<History>k__BackingField",Fields)!.SetValue(owner,new List<HistoryEntry>{new(document,0){HasAudio=false}});
                using var panel=new HistoryPanel(owner);panel.SetActive(true);
                var rows=(StackPanel)typeof(HistoryPanel).GetField("importRows",Fields)!.GetValue(panel)!;
                foreach(var width in new[]{432,760})
                {
                    panel.Measure(new(width,680));panel.Arrange(new Rect(0,0,width,680));panel.UpdateLayout();SettingsHelpChecks.SettleForRender(panel);
                    Require(queue.Items.All(item=>rows.Children.OfType<TextBlock>().Any(text=>text.Text.Contains(item.FileName)&&text.Text.Contains(item.Message))),"History does not show each file's specific error.");
                    Require(!Find<TextBlock>(panel).Any(text=>text.Text.Contains(hidden)),"History exposes a raw import failure detail.");
                    var viewport=Find<ScrollViewer>(panel).Single(view=>ReferenceEquals(view.Content,rows));
                    Require(viewport.ActualHeight<=112.1&&ScrollViewer.GetHorizontalScrollBarVisibility(viewport)==ScrollBarVisibility.Disabled,"Long import failures overflow the bounded queue viewport.");
                    Require(rows.Children.OfType<TextBlock>().All(text=>text.TextWrapping==TextWrapping.Wrap&&text.ActualWidth<=viewport.ActualWidth+.1),"A per-file error fails to wrap inside its queue.");
                    var bitmap=new RenderTargetBitmap(width,680,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,$"import-failures-{theme}-{width}.png"));encoder.Save(file);
                }
                Require(PresentationSource.FromVisual(panel)==null,"Import error inspection opened a native window.");
                checks.Add("Specific per-file import errors and combined API/save failures wrap in a bounded History queue without raw error details "+theme);
            }
        }
        finally{ThemeManager.Apply(originalTheme);}
    }
}
