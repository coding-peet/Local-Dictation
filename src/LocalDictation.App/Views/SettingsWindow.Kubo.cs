using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalDictation.App.Services;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

public sealed partial class SettingsWindow
{
    sealed record KuboChoice(string? Id, string Label, string[]? Efforts = null);

    readonly ComboBox palaKuboModel = new() { DisplayMemberPath = nameof(KuboChoice.Label) };
    readonly ComboBox palaKuboEffort = new() { DisplayMemberPath = nameof(KuboChoice.Label) };
    readonly TextBlock palaKuboStatus = Ui.Text("", 12);
    PalaCodexModel[] kuboCatalog = [];
    Button palaCodexRefresh = null!, palaTranscriptButton = null!;
    bool kuboCatalogRequested, updatingKubo;

    void AddKuboControls(StackPanel group)
    {
        const string modelHelp = "Choose a model from the signed-in Codex account.";
        const string effortHelp = "Choose how much reasoning the model uses. High is the default. Changes apply to the next Kubo request.";

        var processing = new Grid();
        processing.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
        processing.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 155 });
        var model = new StackPanel { Margin = new(0, 0, 12, 0) };
        Ui.Label(model, "Kubo processing model", palaKuboModel);
        AutomationProperties.SetHelpText(palaKuboModel, modelHelp);
        processing.Children.Add(model);

        var effort = new StackPanel();
        Ui.Label(effort, "Reasoning effort", palaKuboEffort);
        AutomationProperties.SetName(palaKuboEffort, "Kubo reasoning effort");
        AutomationProperties.SetHelpText(palaKuboEffort, effortHelp);
        Grid.SetColumn(effort, 1);
        processing.Children.Add(effort);

        var help = SettingsHelp.Create("Kubo processing", modelHelp + " " + effortHelp);
        group.Children.Add(Ui.SettingRow(processing, help));
        palaCodexRefresh = Ui.Button("Refresh Codex models", () => _ = LoadKuboCatalog());
        palaTranscriptButton = Ui.Button("Note transcripts", ShowNoteTranscripts);
        group.Children.Add(Ui.Row(palaCodexRefresh, palaTranscriptButton));
        palaKuboStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        group.Children.Add(palaKuboStatus);
        palaKuboModel.SelectionChanged += (_, _) => SaveKuboChoice(modelChanged: true);
        palaKuboEffort.SelectionChanged += (_, _) => SaveKuboChoice(modelChanged: false);
    }

    async Task LoadKuboCatalog()
    {
        if (!palaCodexRefresh.IsEnabled) return;
        kuboCatalogRequested = true;
        palaCodexRefresh.IsEnabled = false;
        palaKuboStatus.Text = "Loading Codex models…";
        try
        {
            kuboCatalog = await PalaKuboProfile.Models(CancellationToken.None);
            if (closed) return;
            RefreshKuboSelections();
            palaKuboStatus.Text = "";
        }
        catch (Exception)
        {
            if (!closed) palaKuboStatus.Text = "Could not load Codex models. Check Codex sign-in, then refresh.";
        }
        finally
        {
            if (!closed) palaCodexRefresh.IsEnabled = true;
        }
    }

    void RefreshKuboSelections()
    {
        updatingKubo = true;
        try
        {
            var saved = controller.Settings.PalaKuboModel;
            var defaultModel = kuboCatalog.FirstOrDefault(model => model.IsDefault);
            var choices = new List<KuboChoice> { new(null, "Codex default", defaultModel?.Efforts) };
            choices.AddRange(kuboCatalog.Select(model => new KuboChoice(model.Id, model.Label, model.Efforts)));
            if (saved is not null && choices.All(choice => choice.Id != saved))
                choices.Add(new(saved, kuboCatalog.Length == 0 ? saved : $"{saved} · unavailable"));
            SetKuboChoices(palaKuboModel, choices, saved);
            if (palaKuboModel.SelectedItem is KuboChoice choice)
                SetKuboEfforts(choice, controller.Settings.PalaKuboReasoningEffort);
        }
        finally { updatingKubo = false; }
    }

    static void SetKuboChoices(ComboBox box, List<KuboChoice> choices, string? selected)
    {
        if (box.ItemsSource is not IEnumerable<KuboChoice> old || !old.SequenceEqual(choices))
            box.ItemsSource = choices;
        box.SelectedItem = box.Items.Cast<KuboChoice>().First(choice => choice.Id == selected);
    }

    void SetKuboEfforts(KuboChoice model, string selected)
    {
        var available = Settings.KuboReasoningEfforts.Where(effort => model.Efforts?.Contains(effort) ?? true)
            .Select(effort => new KuboChoice(effort, effort == "xhigh" ? "Extra High" : $"{char.ToUpperInvariant(effort[0])}{effort[1..]}"))
            .ToList();
        if (available.All(choice => choice.Id != selected))
            available.Add(new(selected, $"{selected} · unavailable"));
        SetKuboChoices(palaKuboEffort, available, selected);
    }

    void SaveKuboChoice(bool modelChanged)
    {
        if (loading || updatingKubo || palaKuboModel.SelectedItem is not KuboChoice model) return;
        if (modelChanged)
        {
            updatingKubo = true;
            try { SetKuboEfforts(model, controller.Settings.PalaKuboReasoningEffort); }
            finally { updatingKubo = false; }
        }
        if (palaKuboEffort.SelectedItem is not KuboChoice { Id: { } effort }) return;
        if (model.Efforts is { } supported && !supported.Contains(effort))
        {
            palaKuboStatus.Text = "Choose a reasoning level supported by this model.";
            return;
        }
        var settings = Snapshot();
        settings.PalaKuboModel = model.Id;
        settings.PalaKuboReasoningEffort = effort;
        if (Persist(settings)) palaKuboStatus.Text = "";
        else RefreshKuboSelections();
    }

    void ShowNoteTranscripts()
    {
        var dialog = new Window
        {
            Owner = this, Title = "Note transcripts", Width = 750, Height = 570,
            MinWidth = 480, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.SetResourceReference(BackgroundProperty, "Canvas");
        var layout = new Grid { Margin = new(20) };
        foreach (var height in new[] { GridLength.Auto, new GridLength(2, GridUnitType.Star),
                     new GridLength(1, GridUnitType.Star), GridLength.Auto })
            layout.RowDefinitions.Add(new RowDefinition { Height = height });
        var controls = new StackPanel();
        var notes = new ComboBox { DisplayMemberPath = nameof(PalaNoteTranscript.Label) };
        Ui.Label(controls, "Saved Note transcript", notes);
        var info = Ui.Text("", 12);
        controls.Children.Add(info);
        layout.Children.Add(controls);

        TextBox Reader(string label, int row)
        {
            var field = new TextBox
            {
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 8, 0, 8)
            };
            AutomationProperties.SetName(field, label);
            MaterialDesignThemes.Wpf.HintAssist.SetHint(field, label);
            MaterialDesignThemes.Wpf.HintAssist.SetIsFloating(field, true);
            Grid.SetRow(field, row);
            layout.Children.Add(field);
            return field;
        }
        var text = Reader("Original transcript", 1);
        var decisions = Reader("Bridge decisions", 2);
        notes.SelectionChanged += (_, _) =>
        {
            var selected = notes.SelectedItem as PalaNoteTranscript;
            text.Text = selected?.Text ?? "";
            decisions.Text = selected is null ? "" : selected.Decisions is { Length: > 0 } log
                ? string.Join("\n", log) : "No decision log was recorded for this transcript.";
        };

        using var lifetime = new CancellationTokenSource();
        var pageOffsets = new Stack<int>();
        var offset = 0;
        var next = 0;
        Button newer = null!, older = null!;
        async Task Load(int requested, bool moveBack = false)
        {
            newer.IsEnabled = older.IsEnabled = false;
            info.Text = "Loading transcripts…";
            try
            {
                var page = await PalaKuboProfile.Transcripts(requested, lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                if (moveBack) pageOffsets.Pop();
                else if (requested != offset) pageOffsets.Push(offset);
                offset = requested;
                next = page.HasMore ? offset + page.Records.Length : offset;
                notes.ItemsSource = page.Records;
                notes.SelectedIndex = page.Records.Length > 0 ? 0 : -1;
                info.Text = page.Records.Length == 0 ? "Bridge has no saved Note transcripts." : "";
            }
            catch (Exception)
            {
                if (!lifetime.IsCancellationRequested) info.Text = "Could not read the local Bridge transcripts.";
            }
            finally
            {
                if (!lifetime.IsCancellationRequested)
                {
                    newer.IsEnabled = pageOffsets.Count > 0;
                    older.IsEnabled = next > offset;
                }
            }
        }

        newer = Ui.Button("Newer", () => _ = Load(pageOffsets.Peek(), moveBack: true));
        older = Ui.Button("Older", () => _ = Load(next));
        var close = Ui.Button("Close", dialog.Close);
        close.IsCancel = true;
        var buttons = Ui.Row(newer, older, close);
        Grid.SetRow(buttons, 3);
        layout.Children.Add(buttons);
        dialog.Content = layout;
        dialog.Loaded += (_, _) => _ = Load(0);
        dialog.Closed += (_, _) => lifetime.Cancel();
        dialog.ShowDialog();
    }
}
