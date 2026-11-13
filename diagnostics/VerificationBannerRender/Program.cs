using System.Reflection;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class Program
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Property(object target,string name,object? value)=>Set(target,"<"+name+">k__BackingField",value);
    static void Require([DoesNotReturnIf(false)] bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        if(parent is T found)yield return found;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(parent,i)))yield return child;
    }

    static void CheckReplayReceipts(string output,List<string> checks)
    {
        foreach(var sameText in new[]{false,true})
        {
            var controller=(AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
            var parent=new ReviewDocument();parent.SetPrimary(sameText?"same":"old");
            var child=new ReviewDocument();child.SetPrimary(sameText?"same":"new");
            var receipts=new ConfirmedReceiptQueue<PendingPasteConfirmation>();
            var target=(WindowsTextDelivery)RuntimeHelpers.GetUninitializedObject(typeof(WindowsTextDelivery));
            Set(target,"confirmedReceipts",receipts);Set(target,"lifecycleGate",new object());
            var destinations=new Dictionary<string,ITextDelivery>{{parent.Id,target}};
            Set(controller,"destinations",destinations);
            Set(controller,"canonicalReviews",new Dictionary<string,ReviewDocument>{{parent.Id,parent},{child.Id,child}});
            Set(controller,"pending",new PendingReviews(new ReviewStore(Path.Combine(output,"receipt-fixture",Guid.NewGuid().ToString("N")))));
            Property(controller,"History",new List<HistoryEntry>());
            var transfer=typeof(AppController).GetMethod("TransferReplayDestination",Fields)!;
            Require((bool)transfer.Invoke(controller,[parent,child,target])!,"Replay lost a valid original delivery target.");
            Require(destinations.Count==1&&destinations.ContainsKey(child.Id),"Replay left a parent alias for a mutable delivery target.");
            receipts.Publish(new(child.OutputText,true));
            typeof(AppController).GetMethod("DrainConfirmedPastes",Fields)!.Invoke(controller,null);
            Require(child.InsertedAt!=null&&parent.InsertedAt==null,"A late replay receipt was attributed to the wrong Review.");
            Require(receipts.Current==null,"The exact acknowledged replay receipt was not consumed.");
            checks.Add("late replay receipt "+(sameText?"matching":"different")+" text");
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        if(args.Length!=2||args[0]!="--output")throw new ArgumentException("Use --output ABSOLUTE_DIRECTORY");
        var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",Path.Combine(output,"synthetic-home"));
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
            <md:BundledTheme xmlns:md="clr-namespace:MaterialDesignThemes.Wpf;assembly=MaterialDesignThemes.Wpf"
              BaseTheme="Light" PrimaryColor="DeepPurple" SecondaryColor="Pink"/>
            """));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")});
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/LocalDictation;component/Theme.xaml")});
        var checks=new List<string>();
        try
        {
            var invalidSettingsController=(AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
            var savedSettings=new Settings();Property(invalidSettingsController,"Settings",savedSettings);
            Property(invalidSettingsController,"Models",new List<ModelInfo>{new(){Id=savedSettings.PrimaryModel,Name="Slovak",Languages=["sk"]}});
            var rejected=false;
            try{invalidSettingsController.SaveSettings(new Settings{Language="auto",PreferredLanguages=["en"]});}
            catch(ArgumentException){rejected=true;}
            Require(rejected&&ReferenceEquals(invalidSettingsController.Settings,savedSettings),"Invalid Auto preferences replaced the last valid settings.");
            Require(!File.Exists(Path.Combine(AppPaths.Data,"settings.json")),"Invalid Auto preferences were written before validation.");
            checks.Add("invalid preferences preserve saved settings");
            CheckReplayReceipts(output,checks);
            ReviewWorkflowChecks.Insertion(output,checks);
            var cancelledController=(AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
            var cancelledRunType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
            var cancelledRun=RuntimeHelpers.GetUninitializedObject(cancelledRunType);
            using(var cancel=new CancellationTokenSource())
            {
                Set(cancelledRun,"Cancel",cancel);Set(cancelledRun,"RetrySettings",new Settings{Language="en"});
                Set(cancelledRun,"Capture",RuntimeHelpers.GetUninitializedObject(typeof(MicrophoneCapture)));
                Set(cancelledController,"active",cancelledRun);Set(cancelledController,"inserting",new HashSet<string>());
                cancelledController.Cancel();
                Require(cancel.IsCancellationRequested,"Cancel left the existing session running.");
                Require(cancelledRunType.GetField("RetrySettings",Fields)!.GetValue(cancelledRun)==null,
                    "Escape leaves a queued language replay behind.");
                Require((bool)cancelledRunType.GetField("AutoInsertBlocked",Fields)!.GetValue(cancelledRun)!,"Cancel left automatic insertion enabled.");
                checks.Add("cancel queued language replay");
            }
            foreach(var theme in new[]{"Light","Dark"})
            {
                ThemeManager.Apply(theme);
                ReviewWorkflowChecks.Render(theme,output,checks);
                Render(theme,true,false,true,"Toto je prvý prepis. Po kontrole sa vráti na pôvodný kurzor.","verifying-short");
                Render(theme,true,false,true,string.Join(" ",Enumerable.Repeat("Dlhší prvý prepis zostáva počas overovania iba v náhľade bannera.",12)),"verifying-long");
                Render(theme,true,false,false,"Prvý model ešte dokončuje poslednú vetu.","finishing");
                Render(theme,true,true,false,"Live text patrí počas nahrávania do podporovaného cieľového poľa.","recording");
                Render(theme,false,false,false,"Bez Verify sa text vloží priamo.","verify-off");
                Render(theme,true,false,true,"","empty");
                Render(theme,false,true,false,"Recognized speech","auto-detected","auto","en","detected");
                Render(theme,false,true,false,"","auto-waiting","auto");
                Render(theme,false,true,false,"Hola","forced-spanish","es","es","forced");
                Render(theme,false,true,false,"Recognized speech","auto-pending","auto","en","detected",true);
                Render(theme,true,false,true,"First transcript during checking","verifying-auto-pending","auto","en","detected",true);
                Render(theme,false,true,false,"Slovak dictation","model-slovak","sk","sk","model_fixed",false,"kinit/whisper-large-v3-turbo-sk");
                Render(theme,false,true,false,"Custom dictation","model-custom","en","en","forced",false,"local/custom");
                var models=new List<ModelInfo>{new(){Id="multi",Name="Multilingual",Installed=true,Compatible=true,Languages=["sk","en","es","auto"]}};
                var languages=new PreferredLanguagesPanel(["sk","en"],models,_=>throw new Exception("Unexpected remove"),_=>throw new Exception("Unexpected add"));
                languages.Measure(new Size(440,100));languages.Arrange(new Rect(0,0,440,languages.DesiredSize.Height));languages.UpdateLayout();
                Require(languages.Children.OfType<Button>().Count()==3,"Preferred tags must end with one plus button.");
                Require(languages.Children.OfType<Button>().All(b=>b.ActualHeight>=48&&b.ActualWidth>=48),"Language tags lost their 48-DIP target.");
                Save(languages,440,64,"language-tags-"+theme);checks.Add("language-tags "+theme);
                foreach(var prefs in new List<string>[] {new(){"sk","en"},new(),new(){"de"}})
                {
                    var controller=(AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));Property(controller,"Models",models);
                    Property(controller,"Settings",new Settings{PreferredLanguages=prefs});
                    var picker=new LanguageWindow(controller,"multi",true,_=>throw new Exception("Unexpected apply"));
                    var pickerRoot=(FrameworkElement)picker.Content;picker.Content=null;
                    pickerRoot.Measure(new Size(510,double.PositiveInfinity));var height=pickerRoot.DesiredSize.Height;
                    pickerRoot.Arrange(new Rect(0,0,510,height));pickerRoot.UpdateLayout();
                    Require(new WindowInteropHelper(picker).Handle==0,"Language picker created a native window.");
                    var choices=Descendants<ComboBox>(pickerRoot).Single().Items;
                    var expected=prefs.Count==0?4:prefs.Contains("de")?0:3;
                    Require(choices.Count==expected,"Correction picker escaped preferred-language filtering or lost its unrestricted empty state.");
                    if(expected==3)Require(!choices.Cast<object>().Any(c=>c.ToString()!.StartsWith("ES")),"Nonpreferred Spanish leaked into the picker.");
                    var mode=prefs.Count==0?"all":prefs.Contains("de")?"unavailable":"preferred";
                    Save(pickerRoot,510,(int)Math.Ceiling(height),"language-picker-"+mode+"-"+theme);checks.Add("language-picker "+mode+" "+theme);picker.Close();
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,NoProductStartup=true,NoNativeWindowHandle=true,NoInput=true,NoAudio=true,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS {checks.Count} silent WPF state/layout checks; no native window handle, input or audio.");
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=false,Error=error.Message,Checks=checks}));
            Console.Error.WriteLine(error.Message);Environment.ExitCode=1;
        }
        finally{app.Shutdown();}

        void Save(FrameworkElement root,int width,int height,string name)
        {
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(output,name+".png"));png.Save(stream);
        }

        void Render(string theme,bool verify,bool recording,bool verifying,string text,string name,string requested="sk",string? actual=null,string? mode=null,bool pending=false,string primaryModel="openai/whisper-large-v3-turbo")
        {
            // Bypass all product constructors that register hooks, audio devices or stores.
            var controller=(AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
            var runType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
            var run=RuntimeHelpers.GetUninitializedObject(runType);
            var capture=(MicrophoneCapture)RuntimeHelpers.GetUninitializedObject(typeof(MicrophoneCapture));
            var settings=new Settings{VerifyAfterRecording=verify,Language=requested,PrimaryModel=primaryModel,VerificationModel="openai/whisper-large-v3"};
            Set(run,"Settings",settings);Set(run,"Recording",recording);Set(run,"Verifying",verifying);
            Set(run,"ReplayBytes",599L*32000);
            if(pending)Set(run,"RetrySettings",new Settings{Language="auto"});
            Set(run,"PrimaryFinal",verifying);Set(run,"Text",text);Set(run,"Capture",capture);
            Set(run,"Review",new ReviewDocument{Primary=text,PrimaryModel=primaryModel,RequestedLanguage=requested,Language=actual,LanguageMode=mode,Verification="SECOND MODEL MUST NOT REPLACE PREVIEW"});
            Set(run,"DestinationChecked",false); // Refresh must never Show a native banner.
            Set(controller,"active",run);Property(controller,"Settings",settings);Property(controller,"Models",new List<ModelInfo>{
                new(){Id="openai/whisper-large-v3-turbo",Name="Whisper Large-v3 Turbo · Multilingual",Languages=["auto","sk","en","es"]},
                new(){Id="openai/whisper-large-v3",Name="Whisper Large-v3 · Multilingual",Languages=["auto","sk","en","es"]},
                new(){Id="kinit/whisper-large-v3-turbo-sk",Name="Whisper Turbo Slovak · KInIT",Languages=["sk"]},
                new(){Id="local/custom",Name="Workshop custom speech recognizer with a long profile name",Languages=["en"]}});
            Property(controller,"StateLabel",verifying?"Verifying":"Finishing");
            var banner=new BannerWindow(controller);
            try
            {
                banner.Refresh();
                Require(new WindowInteropHelper(banner).Handle==0,"Rendering created a native window handle.");
                var root=(FrameworkElement)banner.Content;banner.Content=null;
                root.Measure(new Size(banner.Width,banner.Height));root.Arrange(new Rect(0,0,banner.Width,banner.Height));root.UpdateLayout();
                // Collapsed ScrollViewer content has no visual subtree until expanded.
                var scroll=Descendants<ScrollViewer>(root).SingleOrDefault();
                Require(scroll!=null,"Preview must use a bounded scroll viewport.");
                var preview=scroll.Content as TextBlock;
                Require(preview!=null&&System.Windows.Automation.AutomationProperties.GetName(preview)=="First transcript preview",
                    "Verification preview is missing from the real banner view.");
                var expected=verify&&!recording&&text.Length>0;
                Require((scroll.Visibility==Visibility.Visible)==expected,"Preview visibility differs from the agreed verification flow.");
                Require(!preview.Focusable&&!scroll.Focusable,"Read-only preview can take keyboard focus.");
                Require(banner.ShowActivated==false,"Banner would activate on show.");
                var language=Descendants<Button>(root).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Change input language");
                Require(!language.Focusable&&language.ActualHeight>=48,"Language indicator can take focus or lost its target.");
                if(controller.IsRecording||controller.IsBusy)
                {
                    var indicator=Descendants<TextBlock>(language).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Elapsed time and input language");
                    Require(indicator.Text.EndsWith(controller.InputLanguageLabel),"Banner omitted the real input language.");
                    Require(indicator.Text.StartsWith(controller.ElapsedLabel),"Processing banner hides the recorded duration.");
                    var formatted=new FormattedText(indicator.Text,System.Globalization.CultureInfo.InvariantCulture,indicator.FlowDirection,
                        new Typeface(indicator.FontFamily,indicator.FontStyle,indicator.FontWeight,indicator.FontStretch),indicator.FontSize,Brushes.Black,1);
                    Require(indicator.ActualWidth+1>=formatted.WidthIncludingTrailingWhitespace,"Banner truncates the elapsed time and language: "+indicator.Text);
                }
                var model=Descendants<TextBlock>(language).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Active transcription model");
                Require(model.Text==controller.BannerModelLabel&&!model.Focusable&&model.ActualWidth>0,"Selected model is missing or focusable.");
                Require((string)model.ToolTip==controller.BannerModelDetails,"Full model identity and role were lost.");
                if(primaryModel!="local/custom")
                {
                    var formattedModel=new FormattedText(model.Text,System.Globalization.CultureInfo.InvariantCulture,model.FlowDirection,
                        new Typeface(model.FontFamily,model.FontStyle,model.FontWeight,model.FontStretch),model.FontSize,Brushes.Black,1);
                    Require(model.ActualWidth+1>=formattedModel.WidthIncludingTrailingWhitespace,"Built-in model label is clipped.");
                }
                Require(new WindowInteropHelper(banner).Handle==0,"Layout created a native window handle.");
                if(expected)
                {
                    Require(preview.Text==text,"Preview changed the first-model transcript.");
                    Require(scroll.ActualHeight<=80.1&&scroll.ActualHeight>0,"Preview viewport must stay within four text lines.");
                    Require(banner.Width==456&&banner.Height==176,"Expanded banner size changed unexpectedly.");
                    if(name.Contains("long"))Require(scroll.ScrollableHeight>0,"Long transcript cannot be scrolled.");
                }
                else Require(banner.Width==392&&banner.Height==88,"Normal recording banner stopped being minimal.");
                var bitmap=new RenderTargetBitmap((int)banner.Width,(int)banner.Height,96,96,PixelFormats.Pbgra32);
                bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using(var stream=File.Create(Path.Combine(output,name+"-"+theme+".png")))png.Save(stream);
                checks.Add(name+" "+theme);
            }
            finally{banner.Close();}
        }
    }
}
