using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

public static class GeneralStartupChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const string Executable=@"C:\Program Files\Local Dictation\LocalDictation.exe";
    public sealed class Store:IWindowsStartupStore
    {
        public object? Value;public int Reads,Writes,Deletes;public bool FailWrite,FailDelete;
        public object? Read(){Reads++;return Value;}
        public void Write(string command){Writes++;if(FailWrite)throw new UnauthorizedAccessException("private failure detail");Value=command;}
        public void Delete(){Deletes++;if(FailDelete)throw new IOException("private failure detail");Value=null;}
    }
    public static Store Inject(AppController owner)
    {
        var store=new Store();typeof(AppController).GetField("startupRegistration",Fields)!.SetValue(owner,new WindowsStartupRegistration(store,Executable));return store;
    }
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static void Call(object target,string name)=>target.GetType().GetMethod(name,Fields,null,Type.EmptyTypes,null)!.Invoke(target,null);
    static IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
    {if(root is T found)yield return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void Layout(FrameworkElement root,int width){root.Measure(new(width,820));root.Arrange(new Rect(0,0,width,820));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);}
    static void Search(SettingsWindow window,MainWindowShell shell,string id,string query,string category)
    {
        shell.Search.Query=query;Layout(shell,1040);
        var entry=Find<ListBox>(shell.Search.ResultsView).Single().Items.Cast<SettingsSearchEntry>().Single(e=>e.Id==id);
        Require(entry.Category==category,"Search retained a stale category for "+id);
        typeof(SettingsWindow).GetMethod("OpenSearchSetting",Fields)!.Invoke(window,[entry]);
    }
    public static void Run(string output,List<string> checks)
    {
        var document=new ReviewDocument();document.SetPrimary("Synthetic saved result.");
        var owner=ReviewWorkflowChecks.Controller(output,document);var store=Inject(owner);
        Set(owner,"<History>k__BackingField",new List<HistoryEntry>{new(document,0){HasAudio=false}});
        var settings=JsonSerializer.Serialize(owner.Settings);var originalTheme=ThemeManager.Choice;
        var settingsPath=Path.Combine(AppPaths.Data,"settings.json");var settingsBytes=File.Exists(settingsPath)?File.ReadAllBytes(settingsPath):null;
        var ctor=typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!;
        var window=(SettingsWindow)ctor.Invoke([owner,false]);
        try
        {
            var shell=(MainWindowShell)window.Content;
            var labels=shell.Navigation.Items.Cast<ListBoxItem>().Select(AutomationProperties.GetName).ToArray();
            Require(labels.SequenceEqual(new[]{"General","Models","Dictation","History","Connections"}),"Connections must be reachable from the main navigation.");
            Require(shell.Navigation.SelectedIndex==(int)MainSection.Dictation,"Manual opening must start on Dictation even though General is first.");
            shell.Navigation.SelectedIndex=(int)MainSection.General;
            var startup=Get<ToggleButton>(window,"startWithWindows");var error=Get<TextBlock>(window,"startupError");
            var service=Get<Expander>(window,"serviceAdvanced");var dictation=Get<Expander>(window,"dictationAdvanced");
            Require(!service.IsExpanded&&!dictation.IsExpanded,"Rare service and transcription recovery controls must start collapsed.");
            Require(startup.IsChecked==false&&startup.IsEnabled&&AutomationProperties.GetName(startup)=="Start with Windows","Startup must be an accessible opt-in switch.");
            var reads=store.Reads;Call(window,"Refresh");Call(window,"Refresh");
            Require(store.Reads==reads&&store.Writes==0&&store.Deletes==0,"Routine controller refresh polls or modifies startup registration.");
            checks.Add("General/Models/Dictation/History/Connections navigation, initial Dictation, opt-in startup and collapsed Advanced without setting changes");

            startup.IsChecked=true;
            Require(startup.IsChecked==true&&store.Value is string command&&command=="\""+Executable+"\" --startup"&&store.Writes==1,"Startup toggle failed to register the explicitly selected executable.");
            startup.IsChecked=false;Require(store.Value==null&&store.Deletes==1,"Startup toggle failed to remove its registration.");
            store.FailWrite=true;startup.IsChecked=true;
            Require(startup.IsChecked==false&&error.Visibility==Visibility.Visible&&error.Text.Length>0&&!error.Text.Contains("private failure detail"),"Startup write failure left a false success or raw exception details.");
            store.FailWrite=false;startup.IsChecked=true;Require(startup.IsChecked==true&&error.Visibility==Visibility.Collapsed,"Explicit startup retry did not recover from a write failure.");
            store.FailDelete=true;startup.IsChecked=false;Require(startup.IsChecked==true&&error.Visibility==Visibility.Visible,"Startup removal failure falsely showed startup disabled.");
            store.FailDelete=false;startup.IsChecked=false;
            var mutations=store.Writes+store.Deletes;
            Call(window,"RefreshStartup");Require(store.Writes+store.Deletes==mutations,"Startup status refresh performed a write.");
            checks.Add("Explicit startup toggle, safe inline write/delete failure, actual-state restoration and retry using only injected registration storage");

            Search(window,shell,"windows-startup","start with windows","General");Require(shell.Navigation.SelectedIndex==(int)MainSection.General,"Startup search missed General.");
            Search(window,shell,"service-address","service address","Connections");Require(service.IsExpanded&&shell.Navigation.SelectedIndex==(int)MainSection.Connections,"Service search failed to expand Connections Advanced.");
            Search(window,shell,"pala-sync","Pala sync","Connections");
            var syncToggle = Get<ToggleButton>(window,"palaIntegration");
            Require(AutomationProperties.GetName(syncToggle)=="Enable Pala sync" &&
                AutomationProperties.GetHelpText(syncToggle).Contains("Wi-Fi"),"Pala sync must explain Wi-Fi.");
            Search(window,shell,"pala-bridge-model","Pala transcription model","Connections");
            Search(window,shell,"pala-note-settings","weather location","Connections");
            var settingsButton = Get<Button>(window,"palaOpenSettings");
            Require(AutomationProperties.GetName(settingsButton)=="Open Note settings" && settingsButton.Focusable &&
                AutomationProperties.GetHelpText(settingsButton).Contains("local browser"),
                "The accessible Note settings action must open the common local browser page.");
            Require(shell.Navigation.SelectedIndex==(int)MainSection.Connections,
                "Bridge settings search did not open Connections.");
            service.IsExpanded = false;
            Layout(shell, 640);
            var title = Find<TextBlock>(shell.PageHost).Single(text => text.Text == "Pala Note Configuration");
            var model = Get<ComboBox>(window, "palaBridgeModel");
            var bridgeStart = Get<Button>(window, "palaStart");
            var bridgeRestart = Get<Button>(window, "palaRestart");
            var bridgeStop = Get<Button>(window, "palaStop");
            var networkStatus = Get<TextBlock>(window, "palaBridgeStatus");
            var bridgeHeading = Find<TextBlock>(shell.PageHost).Single(text => text.Text == "Bridge");
            var track = Get<FrameworkElement>(window, "palaActivityIndicator");
            var activityLine = Get<TextBlock>(window, "palaActivityText");
            Point Center(FrameworkElement element) => element.TranslatePoint(
                new Point(element.ActualWidth / 2, element.ActualHeight / 2), shell.PageHost);
            Require(Center(title).Y < Center(syncToggle).Y &&
                Math.Abs(Center(syncToggle).Y - Center(settingsButton).Y) < 1 &&
                Center(syncToggle).X < Center(settingsButton).X &&
                Center(syncToggle).Y < Center(model).Y && Center(model).Y < Center(bridgeHeading).Y &&
                Math.Abs(Center(bridgeHeading).Y - Center(networkStatus).Y) < 1 &&
                networkStatus.TextAlignment == TextAlignment.Right &&
                Center(bridgeHeading).Y < Center(bridgeStart).Y && Center(bridgeStart).Y < Center(track).Y &&
                Center(track).Y < Center(activityLine).Y && activityLine.TextAlignment == TextAlignment.Right,
                "Connections must align the network status with Bridge and put sync status below the persistent track.");
            var bridgeButtons = new[] { bridgeStart, bridgeRestart, bridgeStop };
            Require(bridgeButtons.Select(button => button.Content as string).SequenceEqual(new[] { "Start", "Restart", "Stop" }) &&
                bridgeButtons.Select(AutomationProperties.GetName).SequenceEqual(new[] { "Start Bridge", "Restart Bridge", "Stop Bridge" }) &&
                bridgeButtons.All(button => Math.Abs(Center(button).Y - Center(bridgeStart).Y) < 1),
                "Short Bridge actions must fit in one row at 640 DIP and keep descriptive accessible names.");

            const string endpoint = "HTTPS 192.0.2.1:8443";
            var bridgeStatusType = typeof(AppController).Assembly.GetType("LocalDictation.App.Services.PalaBridgeController+Status")!;
            var applyBridgeStatus = typeof(SettingsWindow).GetMethod("ApplyPalaBridgeStatus", Fields)!;
            var contact = DateTimeOffset.UtcNow;
            var standalone = Activator.CreateInstance(bridgeStatusType, [$"Standalone Bridge · {endpoint}", null, contact, false, false, endpoint, true]);
            applyBridgeStatus.Invoke(window, [standalone]);
            Require(networkStatus.Text == $"{endpoint} · Standalone" && !bridgeStart.IsEnabled &&
                !bridgeRestart.IsEnabled && !bridgeStop.IsEnabled &&
                networkStatus.ToolTip is string contactHelp && contactHelp.Contains("Last HTTPS contact"),
                "A standalone Bridge must be identified beside the endpoint, keep its contact tooltip, and disable managed actions.");
            var managed = Activator.CreateInstance(bridgeStatusType, [endpoint, null, contact, false, true, endpoint, false]);
            applyBridgeStatus.Invoke(window, [managed]);
            Require(networkStatus.Text == endpoint && !bridgeStart.IsEnabled && bridgeRestart.IsEnabled && bridgeStop.IsEnabled,
                "A managed Bridge must show its endpoint and allow Restart and Stop.");
            Call(window, "RefreshPalaBridgeStatus");
            checks.Add("Short Bridge actions fit at 640 DIP and the heading identifies a standalone Bridge.");

            Require(!Find<TextBlock>(shell.PageHost).Any(text => text.Text.StartsWith("Pairing profile found")),
                "A healthy pairing profile must not occupy a status row.");
            var wasEnabled = owner.Settings.PalaIntegrationEnabled;
            owner.Settings.PalaIntegrationEnabled = false;
            Call(window, "Refresh");
            Require(!settingsButton.IsEnabled && ToolTipService.GetShowOnDisabled(settingsButton) &&
                settingsButton.ToolTip is string explanation && explanation.Contains("Enable Pala sync"),
                "The unavailable Note settings action must explain how to enable it.");
            owner.Settings.PalaIntegrationEnabled = true;
            Call(window, "Refresh");
            Require(settingsButton.IsEnabled, "Enabling Pala sync must enable the Note settings action.");
            owner.Settings.PalaIntegrationEnabled = wasEnabled;
            Call(window, "Refresh");
            checks.Add("Pala sync and Note settings share a row and the button follows the switch.");

            var activityDirectory = Path.Combine(output, "bridge-activity-fixture");
            Directory.CreateDirectory(activityDirectory);
            File.WriteAllText(Path.Combine(activityDirectory, "bridge-activity-usb-fixture.json"),
                JsonSerializer.Serialize(new { Pid = Environment.ProcessId, Transport = "usb",
                    Phase = "transcribing", UpdatedAt = DateTimeOffset.UtcNow }));
            File.WriteAllText(Path.Combine(activityDirectory, "bridge-activity-network-stale.json"),
                JsonSerializer.Serialize(new { Pid = int.MaxValue, Transport = "network",
                    Phase = "processing", UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1) }));
            var activityReader = typeof(AppController).Assembly.GetType("LocalDictation.App.Services.PalaBridgeActivity")!
                .GetMethod("ReadCurrent")!;
            var current = (string?)activityReader.Invoke(null, [activityDirectory]);
            Require(current=="Transcribing on this PC · USB","Bridge activity must accept the live worker and ignore a dead one.");
            // Keep the windowless fixture's phase independent of the running Bridge.
            Get<System.Windows.Threading.DispatcherTimer>(window, "palaActivityPoll").Stop();
            var activity = typeof(SettingsWindow).GetMethod("ApplyPalaActivity",Fields)!;
            var statusLine = Get<TextBlock>(window,"palaActivityText");
            var statusProgress = Get<ProgressBar>(window,"palaActivityProgress");
            activity.Invoke(window, [current, true, SystemParameters.ClientAreaAnimation]);
            var activityIndicator = (FrameworkElement)statusProgress.Parent;
            var activityTrack = Find<Border>(activityIndicator).Single(border => border.Parent == activityIndicator);
            Require(statusLine.Text=="Transcribing on this PC · USB" &&
                statusProgress.IsIndeterminate==SystemParameters.ClientAreaAnimation &&
                activityIndicator.Visibility == Visibility.Visible &&
                statusProgress.Visibility == (SystemParameters.ClientAreaAnimation ? Visibility.Visible : Visibility.Collapsed) &&
                activityIndicator.Height == 10 && statusProgress.Height == activityIndicator.Height,
                "Active Bridge status or reduced-motion behavior is incorrect.");
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ThemeManager.Apply(theme);
                Layout(shell, 1040);
                var helpButton = Find<Button>(shell.PageHost)
                    .Single(button => AutomationProperties.GetName(button) == "About Enable Pala sync");
                // Model a newly opened tooltip in this theme. A disconnected WPF tooltip
                // caches resource lookups until it is attached to a popup again.
                var hoverHelp = SettingsHelp.Create("Enable Pala sync", AutomationProperties.GetHelpText(helpButton));
                var tooltip = (ToolTip)hoverHelp.ToolTip;
                tooltip.Measure(new Size(360, double.PositiveInfinity));
                tooltip.Arrange(new Rect(tooltip.DesiredSize));
                tooltip.UpdateLayout();
                var tooltipImage = new RenderTargetBitmap((int)Math.Ceiling(tooltip.ActualWidth),
                    (int)Math.Ceiling(tooltip.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                tooltipImage.Render(tooltip);
                var tooltipPng = new PngBitmapEncoder();
                tooltipPng.Frames.Add(BitmapFrame.Create(tooltipImage));
                using (var tooltipFile = File.Create(Path.Combine(output, $"pala-sync-tooltip-{theme}.png")))
                    tooltipPng.Save(tooltipFile);

                var surface = Find<Border>(tooltip).First().Background as SolidColorBrush;
                var text = Find<TextBlock>(tooltip).Single().Foreground as SolidColorBrush;
                var expectedSurface = ((SolidColorBrush)Application.Current.FindResource("SurfaceContainerHigh")).Color;
                var expectedText = ((SolidColorBrush)Application.Current.FindResource("Ink")).Color;
                Require(surface?.Color == expectedSurface && text?.Color == expectedText &&
                    (tooltip.Foreground as SolidColorBrush)?.Color == expectedText,
                    $"Pala sync hover help in {theme} used {surface?.Color}/{text?.Color}, expected {expectedSurface}/{expectedText}.");
                checks.Add($"Pala sync hover help uses matching theme colors in {theme}.");

                activity.Invoke(window, [current, true, false]);
                Layout(shell, 1040);
                var accent = ((SolidColorBrush)Application.Current.FindResource("Accent")).Color;
                Require(activityTrack.BorderThickness == new Thickness(2) &&
                    (activityTrack.BorderBrush as SolidColorBrush)?.Color == accent &&
                    activityIndicator.Visibility == Visibility.Visible &&
                    statusProgress.Visibility == Visibility.Collapsed && !statusProgress.IsIndeterminate && statusProgress.Effect == null &&
                    statusLine.Text == current,
                    $"Active Bridge in {theme} with reduced motion must use only the static accent outline and status text.");
                var staticBitmap = new RenderTargetBitmap(1040, 820, 96, 96, PixelFormats.Pbgra32);
                staticBitmap.Render(shell);
                var staticPng = new PngBitmapEncoder();
                staticPng.Frames.Add(BitmapFrame.Create(staticBitmap));
                using (var staticFile = File.Create(Path.Combine(output, $"pala-connections-reduced-motion-{theme}.png")))
                    staticPng.Save(staticFile);

                activity.Invoke(window, [current, true, SystemParameters.ClientAreaAnimation]);
                Layout(shell, 1040);
                var glow = statusProgress.Effect as System.Windows.Media.Effects.DropShadowEffect;
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
                    Require(glow?.Color == ((SolidColorBrush)Application.Current.FindResource("Accent")).Color,
                        "Active Bridge progress did not use the current theme's accent for its glow.");
                else
                    Require(glow == null, "Bridge progress used a glow with reduced motion or high contrast.");
                if (SystemParameters.ClientAreaAnimation)
                {
                    // The detached control does not get WPF's native IsVisible update.
                    Require(VisualStateManager.GoToState(statusProgress, "Indeterminate", false),
                        "Bridge progress has no indeterminate state.");
                    var templateRoot = (FrameworkElement)VisualTreeHelper.GetChild(statusProgress, 0);
                    var state = VisualStateManager.GetVisualStateGroups(templateRoot)
                        .OfType<VisualStateGroup>().Single(group => group.Name == "CommonStates").CurrentState;
                    state.Storyboard.SeekAlignedToLastTick(templateRoot, TimeSpan.FromSeconds(1),
                        System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
                }

                var bitmap = new RenderTargetBitmap(1040, 820, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(shell);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"pala-connections-active-{theme}.png"));
                png.Save(file);
            }
            activity.Invoke(window, ["No sync in progress", false, false]);
            var idleBorder = ((SolidColorBrush)Application.Current.FindResource("Border")).Color;
            Require(statusLine.Text=="No sync in progress" && activityIndicator.Visibility==Visibility.Visible &&
                activityTrack.BorderThickness == new Thickness(1) && (activityTrack.BorderBrush as SolidColorBrush)?.Color == idleBorder &&
                statusProgress.Visibility == Visibility.Collapsed && !statusProgress.IsIndeterminate && statusProgress.Effect == null,
                "Idle Bridge must restore the standard track.");
            checks.Add("Bridge shows a static accent outline with reduced motion, animates when appropriate, and restores the idle track.");
            Search(window,shell,"dictionary-hints","recognition hints","Dictation");Require(dictation.IsExpanded&&shell.Navigation.SelectedIndex==(int)MainSection.Dictation,"Dictionary search failed to reveal Dictation Advanced.");
            dictation.IsExpanded=false;Search(window,shell,"compatibility","application compatibility","Dictation");Require(dictation.IsExpanded,"Compatibility search did not reveal its current target.");
            Require(JsonSerializer.Serialize(owner.Settings)==settings&&store.Writes+store.Deletes==mutations,"Search applied settings or startup registration.");
            window.SelectHistory();Require(shell.Navigation.SelectedIndex==(int)MainSection.History,"History command opened the reordered Dictation page.");
            Require(Get<HistoryPanel>(window,"historyPanel")!=null,"History route lost its persistent panel.");
            checks.Add("Search routes General, Connections, Bridge settings and Dictation, expands Advanced without edits, and preserves reordered History command");

            dictation.IsExpanded=false;owner.RetainDictionaryDraft("Synthetic hints","invalid correction","Synthetic unsaved dictionary warning");Call(window,"RefreshAdvancedStatus");
            var summary=Get<TextBlock>(window,"dictationAdvancedSummary");Require(summary.Visibility==Visibility.Visible&&summary.Text.Contains("Synthetic unsaved dictionary warning"),"Collapsing Advanced hid the unsaved dictionary error.");
            owner.ClearDictionaryDraft();Set(window,"compatibilityWarning","Synthetic compatibility save warning");Call(window,"RefreshAdvancedStatus");Require(summary.Visibility==Visibility.Visible&&summary.Text.Contains("Synthetic compatibility save warning"),"Collapsing Advanced hid compatibility save failure.");
            Set(window,"compatibilityWarning","");Call(window,"RefreshAdvancedStatus");checks.Add("Collapsed Dictation Advanced retains visible unsaved dictionary and compatibility failure summaries");

            foreach(var theme in new[]{"Light","Dark"})foreach(var width in new[]{640,1040})
            {
                ThemeManager.Apply(theme);
                foreach(var section in new[]{MainSection.Connections,MainSection.Dictation})foreach(var expanded in new[]{false,true})
                {
                    service.IsExpanded=dictation.IsExpanded=expanded;shell.Navigation.SelectedIndex=(int)section;Layout(shell,width);
                    var page=(ScrollViewer)shell.PageHost.Content;
                    if(expanded)page.ScrollToBottom();else page.ScrollToHome();Layout(shell,width);
                    if(expanded){var open=section==MainSection.Connections?service:dictation;Require(open.ActualHeight>=((FrameworkElement)open.Content).ActualHeight+40&&((FrameworkElement)open.Content).ActualHeight>=48,"Expanded Advanced content has not reached its visible layout state: "+section+", expander="+open.ActualHeight+", content="+((FrameworkElement)open.Content).ActualHeight+", expanded="+open.IsExpanded);}
                    Require(new WindowInteropHelper(window).Handle==0&&PresentationSource.FromVisual(shell)==null,"General rendering attached a native window.");
                    Require(shell.ActualWidth>=width-32&&shell.ActualWidth<=width&&shell.PageHost.ActualWidth>350,"Section layout lost usable compact content width.");
                    var bitmap=new RenderTargetBitmap(width,820,96,96,PixelFormats.Pbgra32);bitmap.Render(shell);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,$"general-navigation-{section}-{(expanded?"advanced":"top")}-{width}-{theme}.png"));png.Save(file);
                }
                checks.Add($"Connections and Dictation top/Advanced layouts at {width} DIP {theme}, no native window or devices");
            }
            Require(JsonSerializer.Serialize(owner.Settings)==settings&&store.Writes+store.Deletes==mutations,"Rendering modified settings or registration.");
            Require(settingsBytes==null?!File.Exists(settingsPath):File.ReadAllBytes(settingsPath).SequenceEqual(settingsBytes),"General startup UI wrote the dictation settings file.");
        }
        finally{owner.ClearDictionaryDraft();window.Close();ThemeManager.Apply(originalTheme);}
    }
}
