using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using LocalDictation.App.Services;

namespace LocalDictation.App.Views;

/// <summary>
/// Main-window layout without a Window or device/controller dependency. The same
/// chrome can be measured and rendered offline with representative page content.
/// </summary>
public sealed class MainWindowShell : Grid, IDisposable
{
    readonly IReadOnlyList<UIElement> pages;
    readonly Action<int>? pageSelected;
    readonly TextBlock saveStatus;
    bool disposed;

    public SettingsSearchControl Search {get;} = new();
    public FrameworkElement StatusBar {get;}
    public FrameworkElement StatusArea {get;}
    public ContentControl PageHost {get;}
    public ListBox Navigation {get;}

    public MainWindowShell(ListBox navigation, IReadOnlyList<UIElement> pages,
        Button record, Button help, TextBlock connection, TextBlock saveStatus,
        TextBlock message, Action<int>? pageSelected=null)
    {
        if(pages.Count!=4)throw new ArgumentException("Provide Dictation, Models, History and Settings pages.",nameof(pages));
        this.pages=pages;this.pageSelected=pageSelected;this.saveStatus=saveStatus;Navigation=navigation;
        Margin=new(16,8,16,8);SetResourceReference(BackgroundProperty,"Canvas");
        RowDefinitions.Add(new(){Height=GridLength.Auto});
        RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        RowDefinitions.Add(new(){Height=GridLength.Auto});

        var alerts=new StackPanel{Margin=new(0,0,0,8)};StatusArea=alerts;
        message.SetResourceReference(TextBlock.ForegroundProperty,"Warning");
        message.Margin=new(0,0,0,4);message.TextWrapping=TextWrapping.Wrap;
        message.SetBinding(VisibilityProperty,new Binding(nameof(TextBlock.Text)){Source=message,Converter=new MessageVisibilityConverter()});
        System.Windows.Automation.AutomationProperties.SetLiveSetting(message,System.Windows.Automation.AutomationLiveSetting.Polite);
        alerts.Children.Add(message);
        alerts.SetBinding(VisibilityProperty,new Binding(nameof(TextBlock.Text)){Source=message,Converter=new MessageVisibilityConverter()});
        Grid.SetRow(alerts,0);Children.Add(alerts);

        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new(160)});
        body.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        navigation.Margin=new(0,0,8,0);navigation.Padding=new(0);
        navigation.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        navigation.SetResourceReference(Control.BackgroundProperty,"Canvas");
        ScrollViewer.SetHorizontalScrollBarVisibility(navigation,ScrollBarVisibility.Disabled);
        System.Windows.Automation.AutomationProperties.SetName(navigation,"Sections");
        var destinations=new[]{("Dictation",PackIconKind.Microphone),("Models",PackIconKind.CubeOutline),("History",PackIconKind.History),("Settings",PackIconKind.CogOutline)};
        foreach(var (label,icon) in destinations)
        {
            var caption=Ui.Text(label);caption.FontSize=13;
            caption.ClearValue(TextBlock.ForegroundProperty);caption.Margin=new(0);
            caption.TextWrapping=TextWrapping.NoWrap;caption.VerticalAlignment=VerticalAlignment.Center;
            var item=new ListBoxItem{Content=caption,ToolTip=label,Padding=new(0),Margin=new(0,0,0,4),MinHeight=48,Width=double.NaN};
            item.SetResourceReference(StyleProperty,"Md3NavigationItem");
            NavigationDrawerAssist.SetSelectedIcon(item,icon);NavigationDrawerAssist.SetUnselectedIcon(item,icon);
            System.Windows.Automation.AutomationProperties.SetName(item,label);navigation.Items.Add(item);
        }
        PageHost=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        var navigationColumn=new Grid();
        navigationColumn.RowDefinitions.Add(new(){Height=GridLength.Auto});
        navigationColumn.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        navigationColumn.RowDefinitions.Add(new(){Height=GridLength.Auto});
        navigationColumn.RowDefinitions.Add(new(){Height=GridLength.Auto});
        navigationColumn.Children.Add(Search);
        Grid.SetRow(navigation,1);navigationColumn.Children.Add(navigation);
        record.Margin=new(0,8,8,4);record.HorizontalAlignment=HorizontalAlignment.Stretch;record.MinHeight=Math.Max(48,record.MinHeight);
        Grid.SetRow(record,2);navigationColumn.Children.Add(record);
        help.Margin=new(0,0,8,0);help.HorizontalAlignment=HorizontalAlignment.Stretch;help.MinHeight=Math.Max(48,help.MinHeight);help.ToolTip="Help (F1)";
        Grid.SetRow(help,3);navigationColumn.Children.Add(help);
        var pageColumn=new Grid();pageColumn.Children.Add(PageHost);pageColumn.Children.Add(Search.ResultsView);

        // Routine state belongs below the workspace, not above the content.
        // Actionable global alerts retain their separate expanding region above it.
        var statusBar=new Grid{MinHeight=24,Margin=new(0,8,0,0)};StatusBar=statusBar;
        statusBar.ColumnDefinitions.Add(new(){Width=new(2,GridUnitType.Star)});
        statusBar.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        connection.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        connection.FontSize=12;connection.Margin=new(0,0,16,0);connection.TextWrapping=TextWrapping.Wrap;connection.VerticalAlignment=VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(connection,System.Windows.Automation.AutomationLiveSetting.Polite);
        System.Windows.Automation.AutomationProperties.SetName(statusBar,"Application status");
        statusBar.Children.Add(connection);
        saveStatus.FontSize=12;saveStatus.Margin=new(0);saveStatus.HorizontalAlignment=HorizontalAlignment.Right;saveStatus.VerticalAlignment=VerticalAlignment.Center;
        saveStatus.TextAlignment=TextAlignment.Left;saveStatus.TextWrapping=TextWrapping.Wrap;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(saveStatus,System.Windows.Automation.AutomationLiveSetting.Polite);
        saveStatus.SetBinding(VisibilityProperty,new MultiBinding
        {
            Converter=new SaveStatusVisibilityConverter(),
            Bindings={new Binding(nameof(TextBlock.Text)){Source=saveStatus},new Binding(nameof(ListBox.SelectedIndex)){Source=navigation},new Binding(nameof(SettingsSearchControl.IsQueryActive)){Source=Search}}
        });
        Grid.SetColumn(saveStatus,1);statusBar.Children.Add(saveStatus);
        Grid.SetRow(statusBar,2);Children.Add(statusBar);
        Grid.SetColumn(pageColumn,1);body.Children.Add(navigationColumn);body.Children.Add(pageColumn);
        Search.QueryChanged+=SearchQueryChanged;
        Grid.SetRow(body,1);Children.Add(body);
        navigation.Loaded+=NavigationLoaded;navigation.SelectionChanged+=NavigationChanged;
        navigation.AddHandler(UIElement.MouseLeftButtonUpEvent,new MouseButtonEventHandler(NavigationClicked),true);
        ThemeManager.Changed+=RefreshNavigationContrast;
        navigation.SelectedIndex=0;
    }

    void NavigationLoaded(object sender,RoutedEventArgs e)=>RefreshNavigationContrast();
    void NavigationChanged(object sender,SelectionChangedEventArgs e)
    {
        var index=Navigation.SelectedIndex;if(index<0||index>=pages.Count)return;
        if(Search.IsQueryActive)Search.Clear();
        PageHost.Content=pages[index];pageSelected?.Invoke(index);RefreshNavigationContrast();
    }
    void NavigationClicked(object sender,MouseButtonEventArgs args)
    {
        if(Search.IsQueryActive&&args.OriginalSource is DependencyObject source
            &&ItemsControl.ContainerFromElement(Navigation,source) is ListBoxItem)Search.Clear();
    }
    void SearchQueryChanged()
    {
        PageHost.Visibility=Search.IsQueryActive?Visibility.Hidden:Visibility.Visible;
        Search.ResultsView.Visibility=Search.IsQueryActive?Visibility.Visible:Visibility.Collapsed;
        // IsQueryActive is a computed CLR property; query events refresh its binding.
        BindingOperations.GetMultiBindingExpression(saveStatus,VisibilityProperty)?.UpdateTarget();
    }
    void RefreshNavigationContrast()=>ApplyNavigationContrast(Navigation,SystemParameters.HighContrast);

    internal static void ApplyNavigationContrast(ListBox navigation,bool highContrast)
    {
        foreach(var item in navigation.Items.OfType<ListBoxItem>())
        {
            item.ApplyTemplate();
            // Preserve the toolkit's template and its opaque high-contrast pair.
            var frame=item.Template?.FindName("border",item) as Border;
            if(frame!=null)Ui.BindCapsule(frame);
            if(highContrast&&item.IsSelected)
            {
                frame?.SetResourceReference(Border.BackgroundProperty,"Selected");
                item.SetResourceReference(Control.ForegroundProperty,"SelectedInk");
            }
            else
            {
                frame?.ClearValue(Border.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
            }
        }
    }

    public void Dispose()
    {
        if(disposed)return;disposed=true;
        Navigation.Loaded-=NavigationLoaded;Navigation.SelectionChanged-=NavigationChanged;
        Search.QueryChanged-=SearchQueryChanged;
        Navigation.RemoveHandler(UIElement.MouseLeftButtonUpEvent,new MouseButtonEventHandler(NavigationClicked));
        ThemeManager.Changed-=RefreshNavigationContrast;
    }

    sealed class SaveStatusVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values,Type targetType,object parameter,CultureInfo culture)
        {
            if(values[0] is not string text||string.IsNullOrWhiteSpace(text))return Visibility.Collapsed;
            var routine=text.StartsWith("Saved automatically",StringComparison.OrdinalIgnoreCase)||text.Equals("All changes saved",StringComparison.OrdinalIgnoreCase)||text is "Saving…" or "Saving...";
            var relevant=values[1] is int index&&index is 0 or 3&&values[2] is false;
            // Only recognized routine states are contextual. Keep unrecognized and
            // failure messages visible on every page, including search results.
            return relevant||!routine?Visibility.Visible:Visibility.Collapsed;
        }
        public object[] ConvertBack(object value,Type[] targetTypes,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }

    sealed class MessageVisibilityConverter : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>
            value is string text&&!string.IsNullOrWhiteSpace(text)?Visibility.Visible:Visibility.Collapsed;
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
