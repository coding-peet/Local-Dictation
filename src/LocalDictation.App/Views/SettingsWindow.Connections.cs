using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LocalDictation.App.Services;
using System.Windows.Threading;

namespace LocalDictation.App.Views;

public sealed partial class SettingsWindow
{
    sealed record BridgeModelChoice(string? Id, string Label);

    readonly TextBlock palaSettingsError = Ui.Text("", 12);
    Button palaOpenSettings = null!;
    readonly ComboBox palaBridgeModel = new() { DisplayMemberPath = nameof(BridgeModelChoice.Label) };
    readonly System.Windows.Controls.Primitives.ToggleButton palaBridgeAutoStart = Ui.Switch("Start with Local Dictation");
    readonly System.Windows.Controls.Primitives.ToggleButton palaIntegration = Ui.Switch("Enable Pala sync");
    readonly TextBlock palaBridgeStatus = Ui.Text("", 12);
    readonly TextBlock palaBridgeError = Ui.Text("", 12);
    const double PalaActivityBarHeight = 10;
    readonly TextBlock palaActivityText = Ui.Text("No sync in progress");
    readonly Grid palaActivityIndicator = new() { Height = PalaActivityBarHeight, Margin = new(0, 6, 0, 5) };
    readonly Border palaActivityTrack = new() { CornerRadius = new(PalaActivityBarHeight / 2), BorderThickness = new(1) };
    readonly ProgressBar palaActivityProgress = new()
    {
        Height = PalaActivityBarHeight, Background = Brushes.Transparent, BorderThickness = new(0),
        Visibility = Visibility.Collapsed
    };
    readonly DropShadowEffect palaActivityGlow = new() { BlurRadius = 12, ShadowDepth = 0, Opacity = 0.85 };
    readonly DispatcherTimer palaActivityPoll = new() { Interval = TimeSpan.FromMilliseconds(750) };
    readonly DispatcherTimer palaBridgePoll = new() { Interval = TimeSpan.FromSeconds(3) };
    Button palaStart = null!, palaRestart = null!, palaStop = null!;

    UIElement ConnectionsPage()
    {
        ConfigureAdvanced(serviceAdvanced);
        var page = new StackPanel { Margin = new(0, 8, 8, 0) };
        var service = new StackPanel();
        service.Children.Add(Ui.Text("Local transcription service", 18, true));
        service.Children.Add(Ui.Row(
            Ui.Button("Reconnect", () => _ = Reconnect()),
            Ui.Button("Start local API", () => _ = controller.StartLocalApi())));

        var advanced = new StackPanel();
        Ui.Label(advanced, "Local service address", endpoint, "The session service supplies your installed models and transcriptions.");
        serviceAdvanced.Content = Ui.SettingsGroup(advanced);
        service.Children.Add(serviceAdvanced);

        page.Children.Add(Section(service));
        page.Children.Add(PalaBridgeCard());
        return new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxWidth = 880,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    FrameworkElement PalaBridgeCard()
    {
        var group = new StackPanel();
        group.Children.Add(Ui.Text("Pala Note Configuration", 18, true));
        palaOpenSettings = Ui.Button("Open Note settings", () =>
        {
            palaSettingsError.Text = controller.OpenPalaNoteSettings() ? "" :
                "Note settings are unavailable. Check the Bridge runtime or reopen Local Dictation.";
            palaSettingsError.Visibility = palaSettingsError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
        palaOpenSettings.SetResourceReference(FrameworkElement.StyleProperty, "Md3OutlinedButton");
        palaOpenSettings.Margin = new(12, 0, 0, 0);
        palaOpenSettings.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetShowOnDisabled(palaOpenSettings, true);
        AutomationProperties.SetHelpText(palaOpenSettings,
            "Enable Pala sync to open the local browser page for Note settings, Bridge settings, weather and pairing.");
        group.Children.Add(Ui.SwitchRow("Enable Pala sync", palaIntegration,
            "Enable access to Note settings and sync the paired Note over USB or Bluetooth. For Wi-Fi sync, also start Bridge on an approved network.",
            palaOpenSettings));
        palaSettingsError.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        palaSettingsError.Visibility = Visibility.Collapsed;
        group.Children.Add(palaSettingsError);
        Ui.Label(group, "Pala transcription model", palaBridgeModel, "Choose the Dictation model or one of the installed models that supports Slovak.");
        AddKuboControls(group);

        var bridgeHeader = new Grid { Margin = new(0, 16, 0, 0) };
        bridgeHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bridgeHeader.ColumnDefinitions.Add(new());
        var bridgeHeading = Ui.Text("Bridge", 16, true);
        bridgeHeading.Margin = new(0, 0, 12, 0);
        bridgeHeading.VerticalAlignment = VerticalAlignment.Center;
        bridgeHeader.Children.Add(bridgeHeading);
        palaBridgeStatus.Margin = new(0);
        palaBridgeStatus.VerticalAlignment = VerticalAlignment.Center;
        palaBridgeStatus.TextAlignment = TextAlignment.Right;
        palaBridgeStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetColumn(palaBridgeStatus, 1);
        bridgeHeader.Children.Add(palaBridgeStatus);
        group.Children.Add(bridgeHeader);
        palaStart = Ui.Button("Start", () =>
        {
            controller.PalaBridge.Start();
            RefreshPalaBridgeStatus();
        });
        palaRestart = Ui.Button("Restart", () => _ = StopPalaBridge(true));
        palaStop = Ui.Button("Stop", () => _ = StopPalaBridge(false));
        AutomationProperties.SetName(palaStart, "Start Bridge");
        AutomationProperties.SetName(palaRestart, "Restart Bridge");
        AutomationProperties.SetName(palaStop, "Stop Bridge");
        var actions = new WrapPanel();
        actions.Children.Add(palaStart);
        actions.Children.Add(palaRestart);
        actions.Children.Add(palaStop);
        group.Children.Add(actions);
        group.Children.Add(Ui.SwitchRow("Start with Local Dictation", palaBridgeAutoStart,
            "Applies the next time Local Dictation starts. Use Exit in the tray to stop a Bridge started here."));

        palaBridgeError.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        group.Children.Add(palaBridgeError);
        group.Children.Add(BridgeActivityIndicator());
        palaActivityText.Margin = new(0);
        palaActivityText.TextAlignment = TextAlignment.Right;
        AutomationProperties.SetLiveSetting(palaActivityText, AutomationLiveSetting.Polite);
        group.Children.Add(palaActivityText);

        palaIntegration.Checked += (_, _) => SavePalaBridgeSettings();
        palaIntegration.Unchecked += (_, _) => SavePalaBridgeSettings();
        palaBridgeModel.SelectionChanged += (_, _) => SavePalaBridgeSettings();
        palaBridgeAutoStart.Checked += (_, _) => SavePalaBridgeSettings();
        palaBridgeAutoStart.Unchecked += (_, _) => SavePalaBridgeSettings();
        palaBridgePoll.Tick += (_, _) => RefreshPalaBridgeStatus();
        palaActivityPoll.Tick += (_, _) => RefreshPalaActivity();
        return Section(Ui.SettingsGroup(group));
    }

    FrameworkElement BridgeActivityIndicator()
    {
        palaActivityTrack.SetResourceReference(Border.BackgroundProperty, "SurfaceContainerHigh");
        palaActivityTrack.SetResourceReference(Border.BorderBrushProperty, "Border");
        palaActivityIndicator.Children.Add(palaActivityTrack);

        AutomationProperties.SetName(palaActivityProgress, "Bridge activity");
        palaActivityProgress.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignLinearProgressBar");
        palaActivityProgress.SetResourceReference(Control.ForegroundProperty, "Accent");
        BindingOperations.SetBinding(palaActivityGlow, DropShadowEffect.ColorProperty,
            new Binding("Foreground.Color") { Source = palaActivityProgress });
        palaActivityIndicator.Children.Add(palaActivityProgress);
        return palaActivityIndicator;
    }

    async Task StopPalaBridge(bool restart)
    {
        var stopped = controller.PalaBridge.StopAsync(restart);
        RefreshPalaBridgeStatus();
        await stopped;
        if (!closed) RefreshPalaBridgeStatus();
    }

    void RefreshPalaActivity()
    {
        var active = PalaBridgeActivity.ReadCurrent();
        var idle = controller.Settings.PalaIntegrationEnabled ? "No sync in progress" : "Sync is off";
        ApplyPalaActivity(active ?? idle, active != null, SystemParameters.ClientAreaAnimation);
    }

    void ApplyPalaActivity(string text, bool busy, bool animationsEnabled)
    {
        if (palaActivityText.Text != text)
        {
            palaActivityText.Text = text;
            UIElementAutomationPeer.FromElement(palaActivityText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        var animate = busy && animationsEnabled;
        var staticHighlight = busy && !animationsEnabled;
        var outline = new Thickness(staticHighlight ? 2 : 1);
        if (palaActivityTrack.BorderThickness != outline)
        {
            palaActivityTrack.BorderThickness = outline;
            palaActivityTrack.SetResourceReference(Border.BorderBrushProperty, staticHighlight ? "Accent" : "Border");
        }
        palaActivityProgress.IsIndeterminate = animate;
        palaActivityProgress.Effect = animate && !SystemParameters.HighContrast ? palaActivityGlow : null;
        palaActivityProgress.Visibility = animate ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshPalaBridgeStatus() => ApplyPalaBridgeStatus(controller.PalaBridge.ReadStatus());

    void ApplyPalaBridgeStatus(PalaBridgeController.Status status)
    {
        palaBridgeStatus.Text = status.IsStandalone && status.HttpsStatus is { } https
            ? $"{https} · Standalone"
            : status.HttpsStatus ?? status.Summary;
        palaBridgeStatus.ToolTip = status.LastContact is { } seen
            ? $"{status.Summary}\nLast HTTPS contact {seen.ToLocalTime():g}"
            : status.HttpsStatus is not null && status.Summary != status.HttpsStatus ? status.Summary : null;
        var primary = controller.Models.FirstOrDefault(model => model.Id == controller.Settings.PrimaryModel);
        var modelWarning = controller.Settings.PalaBridgeModel == "" && primary is { } &&
            (!primary.Installed || !primary.Compatible || !primary.Languages.Contains("sk"))
            ? "The selected Dictation model is unavailable or does not support Slovak." : null;
        palaBridgeError.Text = string.Join(" ", new[] { status.Error, modelWarning }.OfType<string>());
        palaBridgeError.Visibility = palaBridgeError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        palaStart.IsEnabled = status.CanStart;
        palaRestart.IsEnabled = palaStop.IsEnabled = status.CanStop;
    }

    void RefreshPalaBridgeSettings()
    {
        var settings = controller.Settings;
        palaIntegration.IsChecked = settings.PalaIntegrationEnabled;
        palaOpenSettings.IsEnabled = settings.PalaIntegrationEnabled;
        palaOpenSettings.ToolTip = settings.PalaIntegrationEnabled ? null : "Turn on Enable Pala sync to open Note settings.";
        if (!settings.PalaIntegrationEnabled) palaSettingsError.Visibility = Visibility.Collapsed;

        var legacy = controller.PalaBridge.ProfileModel;
        var models = new List<BridgeModelChoice>
        {
            new(null, legacy == null ? "Use paired Bridge profile" : $"Use paired Bridge profile · {legacy}"),
            new("", "Use Local Dictation model")
        };
        models.AddRange(controller.Models
            .Where(model => model.Installed && model.Compatible && model.Languages.Contains("sk"))
            .Select(model => new BridgeModelChoice(model.Id, model.Label)));
        if (settings.PalaBridgeModel is { Length: > 0 } saved && models.All(model => model.Id != saved))
            models.Add(new(saved, $"{saved} · unavailable"));
        if (palaBridgeModel.ItemsSource is not IEnumerable<BridgeModelChoice> previous || !models.SequenceEqual(previous))
            palaBridgeModel.ItemsSource = models;
        palaBridgeModel.SelectedItem = models.First(model => model.Id == settings.PalaBridgeModel);
        palaBridgeAutoStart.IsChecked = settings.PalaBridgeAutoStart;
        palaBridgeAutoStart.IsEnabled = settings.PalaIntegrationEnabled;
        RefreshKuboSelections();
        if (navigation.SelectedIndex == (int)MainSection.Connections) RefreshPalaBridgeStatus();
    }

    void SavePalaBridgeSettings()
    {
        if (loading || palaBridgeModel.SelectedItem is not BridgeModelChoice model) return;
        var settings = Snapshot();
        settings.PalaIntegrationEnabled = palaIntegration.IsChecked == true;
        settings.PalaBridgeAutoStart = palaBridgeAutoStart.IsChecked == true;
        settings.PalaBridgeModel = model.Id;
        if (!Persist(settings)) Refresh();
    }
}
