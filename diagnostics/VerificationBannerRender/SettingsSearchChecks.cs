using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App.Views;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

static class SettingsSearchChecks
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    public static void Run(string theme, string output, List<string> checks)
    {
        var draft = new TextBox { Text = "An unsaved page value." };
        var pages = new UIElement[] { new Border { Child = draft }, new Border(), new Border(), new Border() };
        var pageChanges = 0;
        var ui = typeof(MainWindowShell).Assembly.GetType("LocalDictation.App.Views.Ui")!;
        var buttonFactory = ui.GetMethod("Button", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        Button ActionButton(string label, bool primary = false) => (Button)buttonFactory.Invoke(null,
            [label, (Action)(() => throw new InvalidOperationException("Search invoked an unrelated header action.")), primary])!;
        var saved = new TextBlock { Text = "All changes saved" };
        saved.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        using var shell = new MainWindowShell(new ListBox(), pages,
            ActionButton("Record", true), ActionButton("Help"),
            new TextBlock { Text = "Connected" }, saved, new TextBlock(), _ => pageChanges++);
        shell.Search.SetEntries(new[]
        {
            new SettingsSearchEntry("primary", "Dictation", "Dictation model", "Choose the model for recognized speech.", "recognition"),
            new SettingsSearchEntry("catalog", "Models", "Model downloads", "Add a speech recognition model from the public catalog."),
            new SettingsSearchEntry("language", "Dictation", "Input language", "Choose Slovak or English.", "slovenčina")
        });
        Layout(shell, 640, 720);
        var box = Find<TextBox>(shell.Search).Single();
        Require(box.ActualWidth >= 130 && box.ActualHeight >= 48,
            "Search has no usable field inside the narrow main window.");
        Require(AutomationProperties.GetName(box) == "Search settings" && box.Focusable,
            "Search is missing its accessible keyboard target.");
        Require(shell.StatusBar.ActualHeight <= 64.1, "Search inflates the compact bottom status bar.");
        Require(shell.Navigation.Items.Count == 4 && shell.Navigation.Items.Cast<ListBoxItem>().All(item => item.ActualWidth >= 140),
            "Search hides or narrows the four named navigation destinations.");
        var originalPageChanges = pageChanges;
        SettingsSearchEntry? selected = null;
        var restoredBeforeCallback = false;
        shell.Search.Selected += entry =>
        {
            selected = entry;
            restoredBeforeCallback = !shell.Search.IsQueryActive && shell.PageHost.Visibility == Visibility.Visible;
        };
        box.Text = "MODEL";
        Layout(shell, 640, 720);
        Require(shell.Search.Query == "MODEL" && shell.Search.IsQueryActive && shell.Search.ResultsView.Visibility == Visibility.Visible,
            "Typing does not reveal same-window search results.");
        Require(shell.PageHost.Visibility != Visibility.Visible && ReferenceEquals(shell.PageHost.Content, pages[0]) && draft.Text == "An unsaved page value.",
            "Search discards the selected page or its unsaved controls.");
        var results = Find<ListBox>(shell.Search.ResultsView).Single();
        Require(results.Items.Count == 2 && ((SettingsSearchEntry)results.Items[0]).Id == "catalog",
            "Search results lose label-prefix ranking.");
        Require(selected == null && pageChanges == originalPageChanges,
            "Typing a query navigates or applies settings before the user selects a result.");
        Require(results.ActualHeight > 400 && results.ActualWidth >= 400 && shell.StatusBar.ActualHeight <= 64.1,
            "Narrow search results do not receive the available content space.");
        Save(shell, 640, 720, Path.Combine(output, "settings-search-compact-" + theme + ".png"));
        Require(Find<SmartHint>(shell.Search).Single().IsHintInFloatingPosition,
            "The rendered populated search field has not settled its real floating hint.");
        checks.Add("same-window search preserves narrow navigation and page state " + theme);

        // These are managed routed events against a detached tree, never native input.
        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new DetachedPresentationSource(), 0, Key.Enter)
            { RoutedEvent = UIElement.PreviewKeyDownEvent });
        Require(selected?.Id == "catalog" && restoredBeforeCallback,
            "Enter does not return the selected setting identity after restoring the page.");
        Require(shell.Search.Query == "" && shell.Search.ResultsView.Visibility == Visibility.Collapsed && draft.Text == "An unsaved page value.",
            "Selecting a result leaves results over the page or changes existing input.");
        checks.Add("search Enter selection restores page before navigation callback " + theme);

        shell.Search.Query = "slovencina";
        Layout(shell, 1040, 720);
        Require(results.Items.Count == 1 && ((SettingsSearchEntry)results.Items[0]).Id == "language",
            "WPF search ignores the diacritic-insensitive matcher.");
        var row = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(0);
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseUpEvent });
        Require(selected?.Id == "language" && !shell.Search.IsQueryActive,
            "Clicking a search result does not navigate to its exact target.");
        shell.Search.Query = "not available";
        Layout(shell, 640, 720);
        Require(results.Items.Count == 0 && Find<TextBlock>(shell.Search.ResultsView).Any(text => text.Text == "No settings found." && text.Visibility == Visibility.Visible),
            "No-match query is missing its clear empty state.");
        Save(shell, 640, 720, Path.Combine(output, "settings-search-empty-" + theme + ".png"));
        shell.Search.Query = "model";
        shell.Navigation.SelectedIndex = 3;
        Require(!shell.Search.IsQueryActive && ReferenceEquals(shell.PageHost.Content, pages[3]),
            "Choosing a named page leaves stale search results in front of it.");
        shell.Search.Query = "model";
        ((ListBoxItem)shell.Navigation.SelectedItem).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseUpEvent });
        Require(!shell.Search.IsQueryActive && ReferenceEquals(shell.PageHost.Content, pages[3]),
            "Clicking the already selected named page leaves search covering that page.");
        shell.Search.Query = "   ";
        Require(!shell.Search.IsQueryActive && shell.Search.ResultsView.Visibility == Visibility.Collapsed,
            "Whitespace query hides the actual settings page.");
        shell.Search.Clear();
        shell.Search.Query = "model";
        shell.Search.SetEntries(new[] { new SettingsSearchEntry("replacement", "Models", "Model", "Updated catalog entry.") });
        Layout(shell, 1040, 720);
        Require(results.Items.Count == 1 && ((SettingsSearchEntry)results.Items[0]).Id == "replacement" && selected?.Id == "language",
            "Replacing search metadata retains stale results or navigates during refresh.");
        Save(shell, 1040, 720, Path.Combine(output, "settings-search-wide-" + theme + ".png"));
        Require(PresentationSource.FromVisual(shell) == null, "Search verification created a native window source.");
        checks.Add("search click, empty state, catalog refresh and page dismissal " + theme);

        var rankedSearch = new SettingsSearchControl();
        rankedSearch.SetEntries(new[]
        {
            new SettingsSearchEntry("downloads", "Models", "Model downloads", ""),
            new SettingsSearchEntry("exact-model", "Dictation", "Model", "")
        });
        rankedSearch.Query = "mo";
        rankedSearch.Query = "model";
        var rankedList = Find<ListBox>(rankedSearch.ResultsView).Single();
        Require(((SettingsSearchEntry)rankedList.SelectedItem).Id == "exact-model",
            "Refining a query leaves an older lower-ranked result selected for Enter.");
        checks.Add("refined search selects its current highest-ranked target " + theme);

        var resources = Application.Current.Resources;
        var previousSelected = resources["Selected"];
        var previousInk = resources["SelectedInk"];
        var previousAccent = resources["Accent"];
        try
        {
            resources["Selected"] = SystemColors.HighlightBrush;
            resources["SelectedInk"] = SystemColors.HighlightTextBrush;
            resources["Accent"] = SystemColors.HighlightBrush;
            var contrastSearch = new SettingsSearchControl();
            contrastSearch.SetEntries(new[] { new SettingsSearchEntry("contrast", "Dictation", "Model", "A readable selected description.") });
            contrastSearch.Query = "model";
            Layout(contrastSearch.ResultsView, 448, 560);
            var contrastList = Find<ListBox>(contrastSearch.ResultsView).Single();
            var contrastRow = (ListBoxItem)contrastList.ItemContainerGenerator.ContainerFromIndex(0);
            Require(Find<TextBlock>(contrastRow).Where(text => text.Text.Length > 0).All(text => text.Foreground is SolidColorBrush brush && brush.Color == SystemColors.HighlightTextColor),
                "Selected search metadata ignores the semantic high-contrast foreground.");
            checks.Add("search selected labels and descriptions retain simulated high contrast " + theme);
        }
        finally
        {
            resources["Selected"] = previousSelected;
            resources["SelectedInk"] = previousInk;
            resources["Accent"] = previousAccent;
        }
    }

    sealed class DetachedPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    static void Layout(FrameworkElement root, int width, int height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    static void Save(FrameworkElement root, int width, int height, string path)
    {
        SettingsHelpChecks.SettleForRender(root);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        png.Save(file);
    }
}
