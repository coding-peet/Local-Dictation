using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

internal static class Program
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const string Turbo="kinit/whisper-large-v3-turbo-sk",Large="openai/whisper-large-v3";
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length!=0&&(args.Length!=2||args[0]!="--report"))
        {Console.Error.WriteLine("Usage: SettingsProbe [--report absolute-json-path]");return 2;}
        var report=args.Length==2?Path.GetFullPath(args[1]):null;
        var originalHome=Environment.GetEnvironmentVariable("LOCAL_DICTATION_HOME");
        var root=Path.Combine(Path.GetTempPath(),"LocalDictation-SettingsProbe-"+Guid.NewGuid().ToString("N"));
        var results=new List<CaseResult>();var started=DateTimeOffset.UtcNow;var exitCode=1;
        var app=new LocalDictation.App.App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async()=>
        {
            try
            {
                await Case("Large selection saves immediately, repairs verifier and survives refresh",async test=>
                {
                    test.Box("primary").SelectedValue=Large;
                    Require(test.Controller.Settings.PrimaryModel==Large,"Selecting Large did not update the effective model without Save.");
                    Require(Settings.Load().PrimaryModel==Large,"Selecting Large was not persisted.");
                    Require(test.Controller.Settings.VerificationModel==Turbo&&test.Controller.Settings.VerifyAfterRecording,
                        "An incompatible verifier must be replaced with an eligible different model.");
                    Require(test.Controller.ModelLabel=="Whisper Large-v3","The idle banner still reports the old model.");
                    test.Refresh();
                    Require((string?)test.Box("primary").SelectedValue==Large&&Settings.Load().PrimaryModel==Large,"A controller refresh reverted the choice.");
                    Require(test.Check("live").IsEnabled&&test.Controller.Settings.Live,"A supported Large live selection was disabled.");
                    await Task.CompletedTask;
                });
                await Case("Microphone, language, toggles, verifier and theme persist independently",async test=>
                {
                    test.Box("microphone").SelectedValue="synthetic-mic";
                    test.Box("language").SelectedItem="en";
                    test.Check("verify").IsChecked=false;test.Check("insert").IsChecked=false;
                    test.Check("live").IsChecked=false;test.Check("shortcutEnabled").IsChecked=false;
                    test.Box("theme").SelectedItem="Dark";
                    var saved=Settings.Load();
                    Require(saved.MicrophoneId=="synthetic-mic"&&saved.Language=="en","Microphone or language was not autosaved.");
                    Require(!saved.VerifyAfterRecording&&!saved.InsertAtCursor&&!saved.Live&&!saved.EnableShortcut,"A toggle change was not autosaved.");
                    Require(saved.Theme=="Dark"&&ThemeManager.Choice=="Dark","Theme preview and saved theme differ.");
                    test.Box("primary").SelectedValue=Large;
                    test.Box("verifier").SelectedValue="synthetic/third";
                    Require(Settings.Load().VerificationModel=="synthetic/third","Verifier selection was not autosaved.");
                    await Task.CompletedTask;
                });
                await Case("Verification details collapse without losing their saved model and return on enable",async test=>
                {
                    test.Box("verifier").SelectedValue="synthetic/third";
                    test.Check("verify").IsChecked=false;
                    Require(CollapsedInTree(test.Box("verifier")),"Disabled verification still occupies the settings page.");
                    Require(Settings.Load().VerificationModel=="synthetic/third"&&!Settings.Load().VerifyAfterRecording,"Collapsing verification discarded its selected model.");
                    test.Refresh();
                    Require(CollapsedInTree(test.Box("verifier")),"Passive refresh expanded disabled verification.");
                    test.Check("verify").IsChecked=true;
                    Require(!CollapsedInTree(test.Box("verifier"))&&test.Box("verifier").IsEnabled,"Enabled verification did not reveal its controls.");
                    Require(Settings.Load().VerificationModel=="synthetic/third"&&Settings.Load().VerifyAfterRecording,"Reopening verification changed its saved choice.");
                    await Task.CompletedTask;
                });
                await Case("Opening with verification off leaves details collapsed without rewriting settings",async test=>
                {
                    test.Check("verify").IsChecked=false;
                    var file=Path.Combine(test.Home,"data","settings.json");var before=File.ReadAllBytes(file);
                    var window=new SettingsWindow(test.Controller);
                    try
                    {
                        Field<DispatcherTimer>(window,"poll").Stop();
                        Require(CollapsedInTree(Field<ComboBox>(window,"verifier")),"Opening Settings exposed disabled verification details.");
                        Require(File.ReadAllBytes(file).SequenceEqual(before),"Opening the simplified settings rewrote stored choices.");
                    }
                    finally{window.Close();}
                    await Task.CompletedTask;
                });
                await Case("Invalid text is never saved and does not block valid toggle changes",async test=>
                {
                    test.Text("endpoint").Text="http://";test.Text("shortcutValue").Text="Ctrl+";
                    Require(Settings.Load().Endpoint=="http://127.0.0.1:48125"&&Settings.Load().Shortcut=="Ctrl+Shift","An intermediate edit was persisted.");
                    await Task.Delay(900);
                    Require(Settings.Load().Endpoint=="http://127.0.0.1:48125"&&Settings.Load().Shortcut=="Ctrl+Shift","Invalid text was persisted after debounce.");
                    test.Check("insert").IsChecked=false;
                    Require(!Settings.Load().InsertAtCursor,"An invalid text draft blocked an independent valid change.");
                    Require(test.AllText().Contains("not saved",StringComparison.OrdinalIgnoreCase),"Invalid text has no visible not-saved feedback.");
                    test.Text("endpoint").Text="http://localhost:48126";test.Text("shortcutValue").Text="Ctrl+Alt+Space";
                    Require(Settings.Load().Endpoint=="http://127.0.0.1:48125","Text was saved before debounce.");
                    await Task.Delay(900);
                    Require(Settings.Load().Endpoint=="http://localhost:48126"&&Settings.Load().Shortcut=="Ctrl+Alt+Space","Valid text was not saved after debounce.");
                    Require(!test.AllText().Contains("not saved",StringComparison.OrdinalIgnoreCase),"Corrected drafts retain stale failure feedback.");
                });
                await Case("Valid text commits on focus loss and closing flushes the last edit",async test=>
                {
                    var endpoint=test.Text("endpoint");endpoint.Text="http://localhost:48127";
                    endpoint.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,endpoint,test.Text("shortcutValue")){RoutedEvent=Keyboard.LostKeyboardFocusEvent});
                    Require(Settings.Load().Endpoint=="http://localhost:48127","Focus loss did not persist a valid address.");
                    test.Text("shortcutValue").Text="Alt+F9";test.Window.Close();
                    Require(Settings.Load().Shortcut=="Alt+F9","Closing discarded a valid pending edit.");
                    await Task.CompletedTask;
                });
                await Case("Settings affect the next run and Stop works with invalid drafts while offline",async test=>
                {
                    var run=test.ActiveRun();var snapshot=Field<Settings>(run,"Settings");
                    test.Box("primary").SelectedValue=Large;
                    Require(Settings.Load().PrimaryModel==Large,"The next-recording model was not persisted during recording.");
                    Require(snapshot.PrimaryModel==Turbo&&test.Controller.ModelLabel=="Whisper Turbo","An active recording changed model mid-run.");
                    test.Text("endpoint").Text="invalid";Set(test.Controller,"<Connected>k__BackingField",false);test.Refresh();
                    var record=Field<Button>(test.Window,"record");
                    Require(record.IsEnabled,"Stop became disabled when the connection dropped.");
                    record.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(!test.Controller.IsRecording,"Invalid settings prevented Stop.");
                    await Task.CompletedTask;
                });
                await Case("A failed save stays visible and never claims the selected model became effective",async test=>
                {
                    var file=Path.Combine(test.Home,"data","settings.json");
                    using(var locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                    {
                        test.Box("primary").SelectedValue=Large;
                        Require(test.Controller.Settings.PrimaryModel==Turbo,"A failed write changed the effective model.");
                        Require(test.AllText().Contains("not saved",StringComparison.OrdinalIgnoreCase),"A failed write has no visible not-saved state.");
                    }
                    test.Box("primary").SelectedValue=Turbo;test.Box("primary").SelectedValue=Large;
                    Require(Settings.Load().PrimaryModel==Large,"A corrected save did not recover.");
                    Require(!test.AllText().Contains("not saved",StringComparison.OrdinalIgnoreCase),"A successful retry retains stale failure feedback.");
                    await Task.CompletedTask;
                });
                await Case("A text save cannot hide an earlier failed model change",async test=>
                {
                    var file=Path.Combine(test.Home,"data","settings.json");
                    using(var locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))test.Box("primary").SelectedValue=Large;
                    Require(test.Controller.Settings.PrimaryModel==Turbo,"Fixture did not leave an unapplied model selection.");
                    test.Text("shortcutValue").Text="Alt+F9";await Task.Delay(900);
                    Require(Settings.Load().Shortcut=="Alt+F9","Valid text was not persisted after the write lock was released.");
                    Require(test.Controller.Settings.PrimaryModel==Large||test.AllText().Contains("not saved",StringComparison.OrdinalIgnoreCase),"A text save hid the failed model selection and falsely claimed it was saved.");
                    if(test.Controller.Settings.PrimaryModel==Large)Require(Settings.Load().PrimaryModel==Large,"Retried model was not durable.");
                });
                await Case("Live capability is independent from latency readiness and unsupported models save batch mode",async test=>
                {
                    test.Box("primary").SelectedValue=Large;
                    Require(test.Check("live").IsEnabled&&test.Check("live").IsChecked==true,"LiveSupported was incorrectly gated by LiveReady.");
                    var hint=Field<TextBlock>(test.Window,"modelStatus").Text;
                    Require((string?)test.Box("primary").SelectedValue==Large&&hint.Contains("latency",StringComparison.OrdinalIgnoreCase),"Unqualified Large live use has no latency hint.");
                    test.Box("primary").SelectedValue="synthetic/batch";
                    Require(!test.Check("live").IsEnabled&&!Settings.Load().Live,"An unsupported live model retained live mode.");
                    test.Box("primary").SelectedValue=Large;test.Check("live").IsChecked=true;
                    Require(Settings.Load().Live,"A supported model could not re-enable live mode.");
                    await Task.CompletedTask;
                });
                await Case("No eligible second model saves a coherent primary choice with explicit verification feedback",async test=>
                {
                    Set(test.Controller,"<Models>k__BackingField",new List<ModelInfo>{Model(Large,"Whisper Large-v3",true,false)});
                    test.Refresh();test.Box("primary").SelectedValue=Large;
                    Require(Settings.Load().PrimaryModel==Large&&!Settings.Load().VerifyAfterRecording,"A missing verifier blocked saving the primary model.");
                    Require(CollapsedInTree(test.Box("verifier")),"Unavailable verification left its optional controls expanded.");
                    Require(Field<TextBlock>(test.Window,"message").Text.Contains("Verification is off",StringComparison.OrdinalIgnoreCase),"Collapsing unavailable verification hid its explanation.");
                    test.Check("verify").IsChecked=true;
                    Require(!test.Check("verify").IsChecked!.Value&&Field<TextBlock>(test.Window,"message").Text.Contains("no other",StringComparison.OrdinalIgnoreCase),"A rejected verification toggle failed silently.");
                    Set(test.Controller,"<Models>k__BackingField",new List<ModelInfo>{Model(Large,"Whisper Large-v3",true,false),Model(Turbo,"Whisper Turbo",true,true)});
                    test.Refresh();
                    Require(Field<TextBlock>(test.Window,"message").Text.Length==0,"Recovered verification retained its unavailable-model warning.");
                    await Task.CompletedTask;
                });
                await Case("An unavailable selected model blocks Record without falling back",async test=>
                {
                    test.Box("primary").SelectedValue="synthetic/missing";
                    Field<Button>(test.Window,"record").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(!test.Controller.IsBusy&&test.Controller.Settings.PrimaryModel==Turbo,"An invalid selected model started or silently replaced the saved model.");
                    Require(test.AllText().Contains("not saved",StringComparison.OrdinalIgnoreCase),"The invalid model selection has no visible save error.");
                    await Task.CompletedTask;
                });
                await Case("An invalid model draft does not block valid appearance and insertion changes",async test=>
                {
                    test.Box("primary").SelectedValue="synthetic/missing";
                    test.Box("theme").SelectedItem="Light";test.Check("insert").IsChecked=false;
                    Require(Settings.Load().Theme=="Light"&&!Settings.Load().InsertAtCursor,"An invalid model draft blocked independent valid settings.");
                    Require(Settings.Load().PrimaryModel==Turbo&&test.AllText().Contains("not saved",StringComparison.OrdinalIgnoreCase),"Invalid model draft was saved or its failure was hidden.");
                    Field<Button>(test.Window,"record").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(!test.Controller.IsBusy,"Record silently used the older saved model after an invalid selection.");
                    await Task.CompletedTask;
                });
                await Case("An unavailable microphone does not block other valid settings and Record requires a microphone",async test=>
                {
                    var saved=Settings.Load();saved.MicrophoneId="disconnected-saved-mic";test.Controller.SaveSettings(saved);
                    Set(test.Window,"loading",true);test.Box("microphone").ItemsSource=Array.Empty<MicrophoneInfo>();Set(test.Window,"loading",false);
                    test.Box("primary").SelectedValue=Large;test.Check("insert").IsChecked=false;test.Box("theme").SelectedItem="Light";
                    Require(Settings.Load().PrimaryModel==Large&&!Settings.Load().InsertAtCursor&&Settings.Load().Theme=="Light","An unavailable microphone blocked an independent valid setting.");
                    Require(Settings.Load().MicrophoneId=="disconnected-saved-mic","Another setting silently replaced the saved microphone ID.");
                    Field<Button>(test.Window,"record").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(!test.Controller.IsBusy,"Record started without an available selected microphone.");
                    await Task.CompletedTask;
                });
                foreach(var microphoneId in new[]{"","synthetic-mic"})
                {
                    var selectedId=microphoneId;
                    await Case($"Selecting {(selectedId.Length==0?"System default":"an active microphone")} clears the missing microphone warning",async test=>
                    {
                        test.MissingMicrophone();
                        test.Box("microphone").SelectedValue=selectedId;
                        Require(test.Controller.Settings.MicrophoneId==selectedId&&Settings.Load().MicrophoneId==selectedId,"The recovered microphone selection was not autosaved.");
                        Require(Field<TextBlock>(test.Window,"message").Text.Length==0,"A valid microphone selection retained the stale missing-microphone warning.");
                        await Task.CompletedTask;
                    });
                }
                await Case("A successful microphone refresh clears its warning without saving a fallback",async test=>
                {
                    test.MissingMicrophone();
                    var file=Path.Combine(test.Home,"data","settings.json");var before=File.ReadAllBytes(file);
                    test.ApplyMicrophones("",true);
                    Require(Field<TextBlock>(test.Window,"message").Text.Length==0,"A successful microphone refresh retained a stale warning.");
                    Require((string?)test.Box("microphone").SelectedValue==""&&File.ReadAllBytes(file).SequenceEqual(before),"A passive refresh silently rewrote the saved microphone.");
                    await Task.CompletedTask;
                });
                await Case("A microphone refresh retains a missing saved choice and its warning",async test=>
                {
                    test.MissingMicrophone();
                    var saved=Settings.Load().MicrophoneId!;
                    test.ApplyMicrophones(saved,true);
                    Require((string?)test.Box("microphone").SelectedValue==saved&&Settings.Load().MicrophoneId==saved,"A refresh silently fell back from the missing saved microphone.");
                    Require(Field<TextBlock>(test.Window,"message").Text.Contains("unavailable",StringComparison.OrdinalIgnoreCase),"A missing saved microphone lost its warning during refresh.");
                    await Task.CompletedTask;
                });
                await Case("A failed microphone refresh keeps a fresh enumeration warning",async test=>
                {
                    test.MissingMicrophone();
                    test.ApplyMicrophones("",false);
                    Require(Field<TextBlock>(test.Window,"message").Text.Contains("microphone access",StringComparison.OrdinalIgnoreCase),"A failed microphone refresh cleared or hid its fresh enumeration warning.");
                    await Task.CompletedTask;
                });
                await Case("Selecting System default cannot hide a fresh microphone enumeration failure",async test=>
                {
                    test.MissingMicrophone();
                    test.ApplyMicrophones(Settings.Load().MicrophoneId!,false);
                    test.Box("microphone").SelectedValue="";
                    Require(Settings.Load().MicrophoneId=="","System default did not autosave after enumeration failed.");
                    Require(Field<TextBlock>(test.Window,"message").Text.Contains("microphone access",StringComparison.OrdinalIgnoreCase),"Selecting System default hid a fresh microphone enumeration failure.");
                    test.ApplyMicrophones("",true);
                    Require(Field<TextBlock>(test.Window,"message").Text.Length==0,"Successful enumeration did not clear its earlier failure.");
                    await Task.CompletedTask;
                });
                await Case("A rejected recording cannot hide enumeration failure on subsequent microphone selection",async test=>
                {
                    test.MissingMicrophone();
                    test.ApplyMicrophones(Settings.Load().MicrophoneId!,false);
                    Field<Button>(test.Window,"record").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(!test.Controller.IsBusy,"Record started with a missing microphone after enumeration failed.");
                    test.Box("microphone").SelectedValue="";
                    Require(Settings.Load().MicrophoneId=="","The default selection after rejected recording was not autosaved.");
                    Require(Field<TextBlock>(test.Window,"message").Text.Length>0,"Rejected recording let microphone selection hide an unresolved enumeration failure.");
                    test.ApplyMicrophones("",true);
                    Require(Field<TextBlock>(test.Window,"message").Text.Length==0,"Successful enumeration did not clear the recording-blocked microphone warning.");
                    await Task.CompletedTask;
                });
                await Case("Selecting an available microphone preserves an unrelated footer message",async test=>
                {
                    test.MissingMicrophone();
                    const string unrelated="The runtime test did not finish. Your existing model selection was preserved.";
                    Field<TextBlock>(test.Window,"message").Text=unrelated;
                    test.ApplyMicrophones("",true);
                    Require(Field<TextBlock>(test.Window,"message").Text==unrelated,"Microphone refresh erased an unrelated footer message.");
                    test.Box("microphone").SelectedValue="synthetic-mic";
                    Require(Settings.Load().MicrophoneId=="synthetic-mic","An unrelated footer message blocked microphone autosave.");
                    Require(Field<TextBlock>(test.Window,"message").Text==unrelated,"Microphone recovery erased an unrelated footer message.");
                    await Task.CompletedTask;
                });
                await Case("Opening with an unavailable saved verifier shows the actual problem without saving a fallback",async test=>
                {
                    var invalid=Settings.Load();invalid.VerificationModel="synthetic/removed-verifier";test.Controller.SaveSettings(invalid);
                    var file=Path.Combine(test.Home,"data","settings.json");var before=File.ReadAllBytes(file);var timestamp=File.GetLastWriteTimeUtc(file);
                    var window=new SettingsWindow(test.Controller);
                    try
                    {
                        Field<DispatcherTimer>(window,"poll").Stop();
                        Require(Field<ComboBox>(window,"verifier").SelectedValue==null,"The UI silently displayed a verifier different from the saved effective choice.");
                        Require(Field<TextBlock>(window,"verificationStatus").Text.Contains("saved",StringComparison.OrdinalIgnoreCase)&&Field<TextBlock>(window,"verificationStatus").Text.Contains("unavailable",StringComparison.OrdinalIgnoreCase),"The invalid saved verifier has no explicit explanation.");
                        Require(test.Controller.Settings.VerificationModel=="synthetic/removed-verifier"&&File.ReadAllBytes(file).SequenceEqual(before)&&File.GetLastWriteTimeUtc(file)==timestamp,"Opening settings silently rewrote the saved verifier.");
                    }
                    finally{window.Close();}
                    await Task.CompletedTask;
                });
                await Case("Offline refresh retains saved settings without writing or inventing model capability",async test=>
                {
                    var settingsFile=Path.Combine(test.Home,"data","settings.json");
                    var before=File.ReadAllBytes(settingsFile);var timestamp=File.GetLastWriteTimeUtc(settingsFile);
                    Set(test.Controller,"<Models>k__BackingField",new List<ModelInfo>());Set(test.Controller,"<Connected>k__BackingField",false);
                    test.Refresh();test.Refresh();
                    Require(File.ReadAllBytes(settingsFile).SequenceEqual(before)&&File.GetLastWriteTimeUtc(settingsFile)==timestamp,"A passive offline refresh rewrote saved settings.");
                    Require(test.Controller.Settings.PrimaryModel==Turbo&&test.Controller.Settings.Live,"Offline refresh changed the saved model or live selection.");
                    Require(Field<TextBlock>(test.Window,"modelStatus").Text.Contains("saved model retained",StringComparison.OrdinalIgnoreCase),"Offline model state has no retained-choice explanation.");
                    await Task.CompletedTask;
                });
                await Case("Focus loss commits one valid field while the other pending field still debounces",async test=>
                {
                    var endpoint=test.Text("endpoint");endpoint.Text="http://localhost:48128";test.Text("shortcutValue").Text="Alt+F9";
                    endpoint.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,endpoint,test.Text("shortcutValue")){RoutedEvent=Keyboard.LostKeyboardFocusEvent});
                    Require(Settings.Load().Endpoint=="http://localhost:48128"&&Settings.Load().Shortcut=="Ctrl+Shift","Focus loss saved the unrelated field too early.");
                    await Task.Delay(900);
                    Require(Settings.Load().Shortcut=="Alt+F9","One field's focus loss stopped another field's autosave timer.");
                });
                if(report!=null)
                {
                    await using var visual=new TestContext(Path.Combine(root,Guid.NewGuid().ToString("N")));
                    visual.Box("primary").SelectedValue=Large;
                    var directory=Path.GetDirectoryName(report)!;Directory.CreateDirectory(directory);
                    visual.Render(Path.Combine(directory,"settings-dictation.png"),0,872,680);
                    visual.Check("verify").IsChecked=false;
                    visual.Render(Path.Combine(directory,"settings-dictation-simple.png"),0,872,680);
                    visual.Render(Path.Combine(directory,"settings-dictation-compact.png"),0,692,480);
                    visual.Render(Path.Combine(directory,"settings-preferences-short.png"),4,716,480);
                    visual.Box("theme").SelectedItem="Light";
                    visual.Render(Path.Combine(directory,"settings-dictation-light.png"),0,872,680);
                }
                exitCode=results.All(r=>r.Passed)?0:1;
            }
            catch(Exception ex){results.Add(new("Probe setup",false,0,ex.ToString()));}
            finally
            {
                Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",originalHome);
                var payload=new{Fixture="Local Dictation SettingsProbe",StartedUtc=started,CompletedUtc=DateTimeOffset.UtcNow,
                    NoMicrophoneCapture=true,NoApiConnection=true,NoNativeInjection=true,Passed=exitCode==0,Results=results};
                var json=JsonSerializer.Serialize(payload,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);
                if(report!=null){Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,json);}
                var fullRoot=Path.GetFullPath(root);var tempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                try
                {
                    if(fullRoot.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(fullRoot).StartsWith("LocalDictation-SettingsProbe-",StringComparison.Ordinal)&&Directory.Exists(fullRoot))
                        await Task.Run(()=>Directory.Delete(fullRoot,true)).WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch(Exception ex){Console.Error.WriteLine("Temporary cleanup did not finish: "+ex.GetType().Name);}
                Environment.Exit(exitCode);
            }
            async Task Case(string name,Func<TestContext,Task> check)
            {
                var clock=Stopwatch.StartNew();
                try{await using var test=new TestContext(Path.Combine(root,Guid.NewGuid().ToString("N")));await check(test);results.Add(new(name,true,clock.Elapsed.TotalMilliseconds,"Passed"));}
                catch(Exception ex){results.Add(new(name,false,clock.Elapsed.TotalMilliseconds,ex.ToString()));}
            }
        }));
        Dispatcher.Run();return exitCode;
    }
    static bool CollapsedInTree(DependencyObject node)
    {
        for(DependencyObject? current=node;current!=null&&current is not Window;current=LogicalTreeHelper.GetParent(current))
            if(current is UIElement element&&element.Visibility==Visibility.Collapsed)return true;
        return false;
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static T Field<T>(object target,string name)=>(T)(target.GetType().GetField(name,Members)!.GetValue(target)!);
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Members)!.SetValue(target,value);
    static ModelInfo Model(string id,string name,bool live,bool ready)=>new(){Id=id,Name=name,Installed=true,Compatible=true,
        RuntimeTested=true,QualityVerified=false,LiveSupported=live,LiveReady=ready,VerificationEligible=true,Languages=["sk","en","auto"]};
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}
    }
    sealed record CaseResult(string Name,bool Passed,double Milliseconds,string Detail);
    sealed class TestContext:IAsyncDisposable
    {
        public AppController Controller{get;}
        public SettingsWindow Window{get;}
        public string Home{get;}
        object? run;
        public TestContext(string home)
        {
            Home=home;Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",home);
            new Settings{PrimaryModel=Turbo,VerificationModel=Large,VerifyAfterRecording=true,MicrophoneId="",Live=true}.Save();
            Controller=new AppController(true);
            foreach(var name in new[]{"timer","reconnectTimer","resumeTimer"})Field<DispatcherTimer>(Controller,name).Stop();
            Set(Controller,"<Connected>k__BackingField",true);
            Set(Controller,"<ConnectionMessage>k__BackingField","Ready · Local transcription");
            Set(Controller,"<Models>k__BackingField",new List<ModelInfo>{Model(Turbo,"Whisper Turbo",true,true),Model(Large,"Whisper Large-v3",true,false),
                Model("synthetic/third","Third compatible model",true,false),Model("synthetic/batch","Batch-only model",false,false),
                new(){Id="synthetic/missing",Name="Missing model",Installed=false,Languages=["sk","en","auto"]}});
            Window=new SettingsWindow(Controller);Field<DispatcherTimer>(Window,"poll").Stop();
            Set(Window,"loading",true);
            Box("microphone").ItemsSource=new List<MicrophoneInfo>{new("","System default microphone"),new("synthetic-mic","Synthetic microphone")};Box("microphone").SelectedValue="";
            Set(Window,"loading",false);
            Layout();
        }
        public void ApplyMicrophones(string selected,bool enumerated)=>typeof(SettingsWindow).GetMethod("ApplyMicrophoneDevices",Members)!.Invoke(Window,
            [enumerated?new List<MicrophoneInfo>{new("synthetic-mic","Synthetic microphone")}:new List<MicrophoneInfo>(),selected,enumerated]);
        public void MissingMicrophone()
        {
            const string missingId="disconnected-settings-probe-mic";
            var saved=Settings.Load();saved.MicrophoneId=missingId;Controller.SaveSettings(saved);
            ApplyMicrophones(missingId,true);
            Require(Field<TextBlock>(Window,"message").Text.Contains("saved microphone is unavailable",StringComparison.OrdinalIgnoreCase),"Fixture did not show the missing saved microphone warning.");
        }
        public ComboBox Box(string name)=>Field<ComboBox>(Window,name);
        public CheckBox Check(string name)=>Field<CheckBox>(Window,name);
        public TextBox Text(string name)=>Field<TextBox>(Window,name);
        public void Refresh()=>typeof(SettingsWindow).GetMethod("Refresh",Members,null,Type.EmptyTypes,null)!.Invoke(Window,null);
        public string AllText(){Layout();return string.Join("\n",Descendants<TextBlock>((DependencyObject)Window.Content).Select(t=>t.Text));}
        void Layout(){var content=(FrameworkElement)Window.Content;content.Measure(new Size(872,680));content.Arrange(new Rect(0,0,872,680));content.UpdateLayout();}
        public object ActiveRun()
        {
            var type=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
            var snapshot=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Controller.Settings))!;
            run=Activator.CreateInstance(type,Members,null,[snapshot],null)!;Set(Controller,"active",run);Refresh();return run;
        }
        public void Render(string path,int tab,double width,double height)
        {
            var content=(FrameworkElement)Window.Content;
            Descendants<TabControl>(content).Single().SelectedIndex=tab;
            content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);
            var background=new DrawingVisual();
            using(var drawing=background.RenderOpen())drawing.DrawRectangle((Brush)Application.Current.FindResource("Canvas"),null,new Rect(0,0,width,height));
            bitmap.Render(background);bitmap.Render(content);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
        }
        public async ValueTask DisposeAsync()
        {
            Window.Close();Controller.Dispose();Field<BannerWindow>(Controller,"banner").Close();
            if(run!=null)
            {await Field<SessionStream>(run,"Stream").DisposeAsync();await Field<MicrophoneCapture>(run,"Capture").DisposeAsync();Field<CancellationTokenSource>(run,"Cancel").Dispose();}
        }
    }
}
