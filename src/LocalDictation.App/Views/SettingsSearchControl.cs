using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

/// <summary>A search field with a separate, same-window results surface for the shell.</summary>
public sealed class SettingsSearchControl : Grid
{
    readonly TextBox queryBox = new() { MinHeight = 48, Padding = new(12, 10, 8, 10) };
    readonly ListBox results = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(0) };
    readonly TextBlock resultStatus = Ui.Text("", 12);
    readonly TextBlock empty = Ui.Text("No settings found.");
    IReadOnlyList<SettingsSearchEntry> entries = [];

    public FrameworkElement ResultsView { get; }
    public string Query { get => queryBox.Text; set => queryBox.Text = value ?? ""; }
    public bool IsQueryActive => !string.IsNullOrWhiteSpace(Query);
    public event Action<SettingsSearchEntry>? Selected;
    public event Action? QueryChanged;

    public SettingsSearchControl()
    {
        Margin = new(0, 0, 8, 12);
        HintAssist.SetHint(queryBox, "Search settings");
        HintAssist.SetIsFloating(queryBox, true);
        TextFieldAssist.SetHasClearButton(queryBox, true);
        queryBox.ToolTip = "Search settings (Ctrl+F)";
        AutomationProperties.SetName(queryBox, "Search settings");
        AutomationProperties.SetHelpText(queryBox, "Find settings by name, category, description or keyword. Press Enter to open the selected result.");
        queryBox.TextChanged += (_, _) => { Refresh(); QueryChanged?.Invoke(); };
        queryBox.PreviewKeyDown += QueryKeyDown;
        Children.Add(queryBox);

        var content = new Grid();
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var title = Ui.Text("Settings search", 20, true);
        content.Children.Add(title);
        resultStatus.Margin = new(0, 0, 0, 12);
        resultStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLiveSetting(resultStatus, AutomationLiveSetting.Polite);
        Grid.SetRow(resultStatus, 1); content.Children.Add(resultStatus);
        var resultArea = new Grid();
        resultArea.Children.Add(results); resultArea.Children.Add(empty);
        Grid.SetRow(resultArea, 2); content.Children.Add(resultArea);
        var surface = new Border { Child = content, Padding = new(16), Visibility = Visibility.Collapsed };
        surface.SetResourceReference(Border.BackgroundProperty, "SurfaceContainer");
        surface.SetResourceReference(Border.CornerRadiusProperty, "ShapeMedium");
        AutomationProperties.SetName(surface, "Settings search results");
        ResultsView = surface;
        AutomationProperties.SetName(results, "Matching settings");
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(results, ScrollBarVisibility.Auto);
        results.ItemTemplate = ResultTemplate();
        var itemStyle = new Style(typeof(ListBoxItem), (Style)Application.Current.FindResource(typeof(ListBoxItem)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 72d));
        itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(SettingsSearchEntry.Label))));
        itemStyle.Setters.Add(new Setter(AutomationProperties.HelpTextProperty, new Binding(nameof(SettingsSearchEntry.Description))));
        results.ItemContainerStyle = itemStyle;
        results.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, args) =>
        {
            if (args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(results, source) is ListBoxItem item
                && item.DataContext is SettingsSearchEntry entry)
            { Select(entry); args.Handled = true; }
        }), true);
        results.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) { SelectCurrent(); args.Handled = true; }
        };
    }

    public void SetEntries(IEnumerable<SettingsSearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        this.entries = entries.ToArray();
        Refresh(preserveSelection: true);
    }

    public void FocusSearch() { queryBox.Focus(); queryBox.SelectAll(); }
    public void Clear() => queryBox.Clear();

    void Refresh(bool preserveSelection = false)
    {
        var previous = preserveSelection ? (results.SelectedItem as SettingsSearchEntry)?.Id : null;
        var matches = SettingsSearch.Find(entries, Query);
        results.ItemsSource = matches;
        results.SelectedItem = matches.FirstOrDefault(entry => entry.Id == previous) ?? matches.FirstOrDefault();
        resultStatus.Text = matches.Count == 1 ? "1 setting" : $"{matches.Count} settings";
        empty.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        results.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ResultsView.Visibility = IsQueryActive ? Visibility.Visible : Visibility.Collapsed;
    }

    void QueryKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { SelectCurrent(); args.Handled = true; }
        else if (args.Key is Key.Down or Key.Up && results.Items.Count > 0)
        {
            if (results.SelectedIndex < 0) results.SelectedIndex = args.Key == Key.Down ? 0 : results.Items.Count - 1;
            results.ScrollIntoView(results.SelectedItem);
            if (results.ItemContainerGenerator.ContainerFromItem(results.SelectedItem) is ListBoxItem item) item.Focus();
            else results.Focus();
            args.Handled = true;
        }
    }

    void SelectCurrent()
    {
        if (results.SelectedItem is SettingsSearchEntry entry) Select(entry);
    }

    void Select(SettingsSearchEntry entry)
    {
        // Restore the actual page before the owner brings its field into view/focus.
        Clear();
        Selected?.Invoke(entry);
    }

    static DataTemplate ResultTemplate()
    {
        var template = new DataTemplate(typeof(SettingsSearchEntry));
        var row = new FrameworkElementFactory(typeof(StackPanel));
        void Add(string property, string ordinaryRole, double size, bool bold = false)
        {
            var copy = new FrameworkElementFactory(typeof(TextBlock));
            copy.SetBinding(TextBlock.TextProperty, new Binding(property));
            copy.SetValue(TextBlock.FontSizeProperty, size);
            copy.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            copy.SetValue(MarginProperty, new Thickness(0, 2, 0, 2));
            if (bold) copy.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(ordinaryRole)));
            style.Triggers.Add(new DataTrigger
            {
                Binding = new Binding(nameof(ListBoxItem.IsSelected)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) },
                Value = true,
                Setters = { new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("SelectedInk")) }
            });
            copy.SetValue(StyleProperty, style);
            row.AppendChild(copy);
        }
        Add(nameof(SettingsSearchEntry.Category), "Muted", 11);
        Add(nameof(SettingsSearchEntry.Label), "Ink", 14, true);
        Add(nameof(SettingsSearchEntry.Description), "Muted", 12);
        template.VisualTree = row;
        return template;
    }
}
