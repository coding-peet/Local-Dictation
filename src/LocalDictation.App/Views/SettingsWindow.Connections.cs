using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
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
    readonly TextBlock palaBridgeStatus = Ui.Text("");
    readonly TextBlock palaBridgeDetail = Ui.Text("", 12);
    readonly TextBlock palaBridgeError = Ui.Text("", 12);
    readonly TextBlock palaActivityText = Ui.Text("Bridge idle", 13, true);
    readonly ProgressBar palaActivityProgress = new() { Height = 4, Margin = new(0, 5, 0, 0) };
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
        var activityRow = new StackPanel { Margin = new(0, 4, 0, 8) };
        AutomationProperties.SetLiveSetting(palaActivityText, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(palaActivityProgress, "Bridge activity");
        palaActivityProgress.SetResourceReference(Control.ForegroundProperty, "Accent");
        palaActivityProgress.SetResourceReference(Control.BackgroundProperty, "Selected");
        palaActivityProgress.Visibility = Visibility.Hidden;
        activityRow.Children.Add(palaActivityText);
        activityRow.Children.Add(palaActivityProgress);
        group.Children.Add(activityRow);
        palaOpenSettings = Ui.Button("Open Note settings", () =>
        {
            palaSettingsError.Text = controller.OpenPalaNoteSettings() ? "" :
                controller.Settings.PalaIntegrationEnabled
                    ? "Note settings are unavailable. Check the Bridge runtime or reopen Local Dictation."
                    : "Turn on Enable Pala sync to open Note settings.";
            palaSettingsError.Visibility = palaSettingsError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
        AutomationProperties.SetHelpText(palaOpenSettings,
            "Open the local browser page for Note settings, Bridge settings, weather and pairing.");
        group.Children.Add(Ui.Row(palaOpenSettings));
        palaSettingsError.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        palaSettingsError.Visibility = Visibility.Collapsed;
        group.Children.Add(palaSettingsError);
        group.Children.Add(Ui.SwitchRow("Enable Pala sync", palaIntegration,
            "Sync the paired Note over USB or Bluetooth while Local Dictation runs. For Wi-Fi, also start Bridge on an approved network."));
        group.Children.Add(palaBridgeStatus);
        group.Children.Add(palaBridgeDetail);
        palaBridgeError.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        group.Children.Add(palaBridgeError);
        palaStart = Ui.Button("Start Bridge", () =>
        {
            controller.PalaBridge.Start();
            RefreshPalaBridgeStatus();
        });
        palaRestart = Ui.Button("Restart Bridge", () => _ = StopPalaBridge(true));
        palaStop = Ui.Button("Stop Bridge", () => _ = StopPalaBridge(false));
        group.Children.Add(Ui.Row(palaStart, palaRestart, palaStop));
        Ui.Label(group, "Pala transcription model", palaBridgeModel, "Choose the Dictation model or one of the installed models that supports Slovak.");
        group.Children.Add(Ui.SwitchRow("Start with Local Dictation", palaBridgeAutoStart,
            "Applies the next time Local Dictation starts. Use Exit in the tray to stop a Bridge started here."));
        palaIntegration.Checked += (_, _) => SavePalaBridgeSettings();
        palaIntegration.Unchecked += (_, _) => SavePalaBridgeSettings();
        palaBridgeModel.SelectionChanged += (_, _) => SavePalaBridgeSettings();
        palaBridgeAutoStart.Checked += (_, _) => SavePalaBridgeSettings();
        palaBridgeAutoStart.Unchecked += (_, _) => SavePalaBridgeSettings();
        palaBridgePoll.Tick += (_, _) => RefreshPalaBridgeStatus();
        palaActivityPoll.Tick += (_, _) => RefreshPalaActivity();
        return Section(Ui.SettingsGroup(group));
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
        var idle = controller.Settings.PalaIntegrationEnabled ? "Bridge idle" : "Pala sync disabled";
        ApplyPalaActivity(active ?? idle, active != null);
    }

    void ApplyPalaActivity(string text, bool busy)
    {
        if (palaActivityText.Text != text)
        {
            palaActivityText.Text = text;
            UIElementAutomationPeer.FromElement(palaActivityText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        var animate = busy && SystemParameters.ClientAreaAnimation;
        palaActivityProgress.IsIndeterminate = animate;
        palaActivityProgress.Visibility = animate ? Visibility.Visible : Visibility.Hidden;
    }

    void RefreshPalaBridgeStatus()
    {
        var status = controller.PalaBridge.ReadStatus();
        palaBridgeStatus.Text = "Network Bridge · " + status.Summary;
        var pairing = controller.PalaBridge.HasProfile ? "Pairing profile found" : "Pairing profile missing";
        var last = status.LastContact is { } seen ? $" · Last HTTPS contact {seen.ToLocalTime():g}" : "";
        palaBridgeDetail.Text = pairing + last;
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
