using LocalDictation.App.Services;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using System.IO;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.Core;

if(args.Length==2&&args[0]=="--render-compact-review")
{
    Exception? failed=null;
    var thread=new Thread(()=>
    {
        try
        {
            var app=new LocalDictation.App.App();app.InitializeComponent();
            using var controller=new AppController(true);
            var doc=new ReviewDocument{PrimaryModel="kinit/whisper-large-v3-turbo-sk",VerificationModel="openai/whisper-large-v3",VerificationCompleted=true,
                Primary="Chcem otestovať novú verziu a uložiť nahrávku.",Verification="Chcem otestovať aktuálnu verziu a uložiť poznámku.",
                Reason="Choose the correct wording. The last choice returns the text to the original cursor.",
                Pieces=[new("a","Chcem otestovať ","Chcem otestovať ",false),new("first","novú","aktuálnu",true),new("b"," verziu a uložiť "," verziu a uložiť ",false),new("second","nahrávku","poznámku",true),new("c",".",".",false)]};
            var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            typeof(AppController).GetMethod("ShowBannerReview",flags)!.Invoke(controller,[doc]);
            var banner=(BannerWindow)typeof(AppController).GetField("banner",flags)!.GetValue(controller)!;
            banner.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(banner.ActualWidth),(int)Math.Ceiling(banner.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(banner);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var output=File.Create(args[1]))encoder.Save(output);
            banner.Close();app.Shutdown();
        }
        catch(Exception e){failed=e;}
    });
    thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
    if(failed!=null)throw failed;
    Console.WriteLine("Rendered compact Review with synthetic text; no audio or insertion.");return;
}

if(args.Length==2&&args[0]=="--render-review")
{
    Exception? failed=null;
    var thread=new Thread(()=>
    {
        try
        {
            var app=new LocalDictation.App.App();app.InitializeComponent();ThemeManager.Apply("Dark");
            var doc=new ReviewDocument{PrimaryModel="kinit/whisper-large-v3-turbo-sk",VerificationModel="openai/whisper-large-v3",
                Primary="Máme pripravenú novú verziu aplikácie.",Verification="Máme pripravenú aktuálnu verziu aplikácie.",VerificationCompleted=true,
                Reason="The live draft is held in Review. Accept and insert returns the chosen wording to its original place.",
                Pieces=[new("before","Máme pripravenú ","Máme pripravenú ",false),new("choice","novú","aktuálnu",true),new("after"," verziu aplikácie."," verziu aplikácie.",false)]};
            var window=new ReviewWindow(doc,_=>{},_=>{},_=>Task.FromResult(new ReviewInsertionResult(false,"Synthetic preview only.")),_=>{}){ShowActivated=false};
            ThemeManager.TitleBar(window);
            window.Show();window.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var output=File.Create(args[1]))encoder.Save(output);
            window.Close();app.Shutdown();
        }
        catch(Exception e){failed=e;}
    });
    thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
    if(failed!=null)throw failed;
    Console.WriteLine("Rendered synthetic Review preview without recording or delivery.");return;
}

if(args.Length==2&&args[0] is "--render-banner" or "--render-review-banner")
{
    Exception? failed=null;
    var thread=new Thread(()=>
    {
        try
        {
            var app=new LocalDictation.App.App();app.InitializeComponent();
            using var controller=new AppController(true);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var runType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
            var run=runType.GetConstructors(flags).Single().Invoke([controller.Settings]);
            var capture=(MicrophoneCapture)runType.GetField("Capture",flags)!.GetValue(run)!;
            typeof(MicrophoneCapture).GetProperty("CapturedBytes")!.SetValue(capture,12L*32000);
            typeof(MicrophoneCapture).GetProperty("Level")!.SetValue(capture,.1f);
            typeof(MicrophoneCapture).GetProperty("DeviceName")!.SetValue(capture,"Microphone Array");
            var atCursor=args[0]=="--render-banner";
            runType.GetField("CanDeliver",flags)!.SetValue(run,atCursor);
            runType.GetField("Text",flags)!.SetValue(run,"Toto je priebežný prepis. Text zostáva viditeľný aj po zmene kurzora a po skončení ho môžem skontrolovať v Review.");
            ((ReviewDocument)runType.GetField("Review",flags)!.GetValue(run)!).Reason="You clicked outside the recording controls. Automatic insertion stopped; dictation continues here.";
            typeof(AppController).GetField("active",flags)!.SetValue(controller,run);
            typeof(AppController).GetProperty("StateLabel")!.SetValue(controller,"Listening");
            typeof(AppController).GetProperty("Models")!.SetValue(controller,new List<ModelInfo>{new(){Id=controller.Settings.PrimaryModel,Name="KInIT Turbo · Slovak",Installed=true}});
            var banner=(BannerWindow)typeof(AppController).GetField("banner",flags)!.GetValue(controller)!;
            banner.Refresh();banner.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(banner.ActualWidth),(int)Math.Ceiling(banner.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(banner);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var output=File.Create(args[1]))encoder.Save(output);
            banner.Close();typeof(AppController).GetField("active",flags)!.SetValue(controller,null);
            capture.DisposeAsync().AsTask().GetAwaiter().GetResult();app.Shutdown();
        }
        catch(Exception e){failed=e;}
    });
    thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
    if(failed!=null)throw failed;
    Console.WriteLine("Rendered synthetic banner preview without recording audio.");return;
}

var capture=new MicrophoneCapture();
var failure="";
capture.Faulted+=message=>failure=message;
try
{
    capture.Start(null);await Task.Delay(450);capture.Stop();
}
catch(Exception e){failure=e.ToString();}
finally {await capture.DisposeAsync();}
var audio=capture.TakeAudio();
Console.WriteLine(JsonSerializer.Serialize(new{Device=capture.DeviceName,Bytes=audio.Length,Failure=failure}));
CryptographicOperations.ZeroMemory(audio);
if(failure.Length>0||audio.Length==0)Environment.ExitCode=1;
