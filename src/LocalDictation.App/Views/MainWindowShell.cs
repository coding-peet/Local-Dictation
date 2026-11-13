using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    bool disposed;

    public FrameworkElement Header {get;}
    public FrameworkElement StatusArea {get;}
    public ContentControl PageHost {get;}
    public ListBox Navigation {get;}

    public MainWindowShell(ListBox navigation, IReadOnlyList<UIElement> pages,
        Button record, Button help, TextBlock connection, TextBlock saveStatus,
        TextBlock message, Button recovery, Action<int>? pageSelected=null)
    {
        if(pages.Count!=4)throw new ArgumentException("Provide Dictation, Models, History and Settings pages.",nameof(pages));
        this.pages=pages;this.pageSelected=pageSelected;Navigation=navigation;
        Margin=new(16,0,16,12);SetResourceReference(BackgroundProperty,"Canvas");
        RowDefinitions.Add(new(){Height=GridLength.Auto});
        RowDefinitions.Add(new(){Height=GridLength.Auto});
        RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});

        // Normal status fits inside the 64-DIP header. Longer connection or saving
        // failures may grow it: their instructions must remain readable at 640 DIP.
        var header=new Grid{MinHeight=64};Header=header;
        header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var heading=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,4,12,4)};
        var title=Ui.Text("Local Dictation",20,true);title.FontSize=18;title.Margin=new(0,0,0,2);
        heading.Children.Add(title);
        var statusRow=new Grid();
        statusRow.ColumnDefinitions.Add(new(){Width=new(3,GridUnitType.Star)});
        statusRow.ColumnDefinitions.Add(new(){Width=new(2,GridUnitType.Star)});
        connection.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        connection.FontSize=12;connection.Margin=new(0);connection.TextWrapping=TextWrapping.Wrap;
        statusRow.Children.Add(connection);
        saveStatus.FontSize=12;saveStatus.Margin=new(12,0,0,0);
        saveStatus.TextAlignment=TextAlignment.Right;saveStatus.TextWrapping=TextWrapping.Wrap;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(saveStatus,System.Windows.Automation.AutomationLiveSetting.Polite);
        Grid.SetColumn(saveStatus,1);statusRow.Children.Add(saveStatus);heading.Children.Add(statusRow);
        header.Children.Add(heading);
        record.Margin=new(0,0,8,0);help.Margin=new(0);help.ToolTip="Help (F1)";
        var actions=Ui.Row(record,help);actions.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(actions,1);header.Children.Add(actions);Children.Add(header);

        var alerts=new StackPanel{Margin=new(0,0,0,8)};StatusArea=alerts;
        message.SetResourceReference(TextBlock.ForegroundProperty,"Warning");
        message.Margin=new(0,0,0,4);message.TextWrapping=TextWrapping.Wrap;
        message.SetBinding(VisibilityProperty,new Binding(nameof(TextBlock.Text)){Source=message,Converter=new MessageVisibilityConverter()});
        System.Windows.Automation.AutomationProperties.SetLiveSetting(message,System.Windows.Automation.AutomationLiveSetting.Polite);
        recovery.HorizontalAlignment=HorizontalAlignment.Left;recovery.Margin=new(0);
        alerts.Children.Add(message);alerts.Children.Add(recovery);
        var alertVisibility=new MultiBinding{Converter=new AlertVisibilityConverter()};
        alertVisibility.Bindings.Add(new Binding(nameof(TextBlock.Text)){Source=message});
        alertVisibility.Bindings.Add(new Binding(nameof(Visibility)){Source=recovery});
        alerts.SetBinding(VisibilityProperty,alertVisibility);
        Grid.SetRow(alerts,1);Children.Add(alerts);

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
        Grid.SetColumn(PageHost,1);body.Children.Add(navigation);body.Children.Add(PageHost);
        Grid.SetRow(body,2);Children.Add(body);
        navigation.Loaded+=NavigationLoaded;navigation.SelectionChanged+=NavigationChanged;
        ThemeManager.Changed+=RefreshNavigationContrast;
        navigation.SelectedIndex=0;
    }

    void NavigationLoaded(object sender,RoutedEventArgs e)=>RefreshNavigationContrast();
    void NavigationChanged(object sender,SelectionChangedEventArgs e)
    {
        var index=Navigation.SelectedIndex;if(index<0||index>=pages.Count)return;
        PageHost.Content=pages[index];pageSelected?.Invoke(index);RefreshNavigationContrast();
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
        ThemeManager.Changed-=RefreshNavigationContrast;
    }

    sealed class MessageVisibilityConverter : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>
            value is string text&&!string.IsNullOrWhiteSpace(text)?Visibility.Visible:Visibility.Collapsed;
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    sealed class AlertVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values,Type targetType,object parameter,CultureInfo culture)=>
            values[0] is string text&&!string.IsNullOrWhiteSpace(text)||values[1] is Visibility.Visible?Visibility.Visible:Visibility.Collapsed;
        public object[] ConvertBack(object value,Type[] targetTypes,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
