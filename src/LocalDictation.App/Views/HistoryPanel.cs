using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

public sealed class HistoryPanel : UserControl,IDisposable
{
    readonly AppController controller;
    readonly ListBox items=new(){MinHeight=150};
    readonly TextBox transcript=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top,FontSize=15};
    readonly TextBlock title=Ui.Text("",16,true),state=Ui.Text("",12),detail=Ui.Text("",12),language=Ui.Text("",12),count=Ui.Text("",12),message=Ui.Text("");
    readonly HistoryAudioPlayerControl playback=new();
    readonly ContextMenu moreMenu=new();
    readonly Button openReview,copyText,more;
    readonly List<Control> audioActions=[];
    Func<DictationItem,bool> confirmDelete;
    Func<Window?,DictationItem,bool> showDeleteConfirmation=DeleteDictationDialog.Confirm;
    bool refreshing,active,disposed;
    string? loadedAudioId;
    IReadOnlyList<DictationItem>? shown;

    public HistoryPanel(AppController controller)
    {
        this.controller=controller;confirmDelete=ConfirmDelete;
        // Keep the current toolkit/theme template; only History's scrollbar
        // dimensions change. These resources do not affect other application pages.
        var scrollStyle=new Style(typeof(ScrollBar),TryFindResource(typeof(ScrollBar)) as Style);
        scrollStyle.Setters.Add(new Setter(MinWidthProperty,0d));scrollStyle.Setters.Add(new Setter(MinHeightProperty,0d));
        scrollStyle.Triggers.Add(new Trigger{Property=ScrollBar.OrientationProperty,Value=Orientation.Vertical,Setters={new Setter(WidthProperty,10d),new Setter(HeightProperty,double.NaN)}});
        scrollStyle.Triggers.Add(new Trigger{Property=ScrollBar.OrientationProperty,Value=Orientation.Horizontal,Setters={new Setter(HeightProperty,10d),new Setter(WidthProperty,double.NaN)}});
        Resources[typeof(ScrollBar)]=scrollStyle;Resources[SystemParameters.VerticalScrollBarWidthKey]=10d;Resources[SystemParameters.HorizontalScrollBarHeightKey]=10d;
        var root=new DockPanel{MinHeight=420,Height=420};root.SetResourceReference(Panel.BackgroundProperty,"Canvas");
        var footer=new StackPanel{Margin=new(0,8,0,0)};
        playback.PlaybackError+=text=>message.Text=text;footer.Children.Add(playback);
        message.Margin=new(0,4,0,0);message.SetResourceReference(TextBlock.ForegroundProperty,"Warning");
        var messageStyle=new Style(typeof(TextBlock),message.Style);
        messageStyle.Triggers.Add(new Trigger{Property=TextBlock.TextProperty,Value="",Setters={new Setter(VisibilityProperty,Visibility.Collapsed)}});message.Style=messageStyle;
        footer.Children.Add(message);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);

        var body=new Grid{MinHeight=220};
        var listColumn=new ColumnDefinition{Width=new(212),MinWidth=160};var dividerColumn=new ColumnDefinition{Width=new(10)};var detailColumn=new ColumnDefinition{Width=new(1,GridUnitType.Star),MinWidth=240};
        body.ColumnDefinitions.Add(listColumn);body.ColumnDefinitions.Add(dividerColumn);body.ColumnDefinitions.Add(detailColumn);
        body.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var splitter=new GridSplitter{Width=10,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,ShowsPreview=false,KeyboardIncrement=16,DragIncrement=1,Focusable=true,Cursor=Cursors.SizeWE,Template=DividerTemplate(),ToolTip="Resize dictations list"};
        splitter.SetResourceReference(Control.FocusVisualStyleProperty,"Md3FocusVisual");
        AutomationProperties.SetName(splitter,"Resize dictations list");AutomationProperties.SetHelpText(splitter,"Drag to resize the list and transcript. Left and Right resize the columns with the keyboard.");
        Grid.SetColumn(splitter,1);body.Children.Add(splitter);
        var listArea=new DockPanel();
        var itemStyle=new Style(typeof(ListBoxItem),(Style)FindResource(typeof(ListBoxItem)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));items.ItemContainerStyle=itemStyle;
        items.ItemTemplate=RowTemplate();var list=Ui.Card(items);list.Padding=new(0);list.Margin=new(0);listArea.Children.Add(list);body.Children.Add(listArea);

        var right=new Grid();right.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});right.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var selectedText=new DockPanel();var metadata=new StackPanel();var detailHeading=new DockPanel{Margin=new(0,0,0,4)};
        count.Margin=new(12,0,0,0);count.VerticalAlignment=VerticalAlignment.Center;count.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        DockPanel.SetDock(count,Dock.Right);detailHeading.Children.Add(count);
        title.Margin=new(0);title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;detailHeading.Children.Add(title);metadata.Children.Add(detailHeading);
        var metadataRow=new Grid{Margin=new(0,0,0,12)};metadataRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});metadataRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});metadataRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        state.Margin=new(0,0,12,0);state.SetResourceReference(TextBlock.ForegroundProperty,"Accent");metadataRow.Children.Add(state);
        detail.Margin=new(0);detail.TextWrapping=TextWrapping.NoWrap;detail.TextTrimming=TextTrimming.CharacterEllipsis;detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(detail,1);metadataRow.Children.Add(detail);
        language.Margin=new(12,0,0,0);language.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(language,2);metadataRow.Children.Add(language);metadata.Children.Add(metadataRow);
        DockPanel.SetDock(metadata,Dock.Top);selectedText.Children.Add(metadata);selectedText.Children.Add(transcript);
        var card=Ui.Card(selectedText);card.Padding=new(16,12,16,12);card.Margin=new(0);right.Children.Add(card);

        openReview=Ui.Button("Open Review",OpenReview,true);copyText=Ui.Button("Copy text",Copy);copyText.SetResourceReference(StyleProperty,"Md3OutlinedButton");
        more=Ui.IconButton(PackIconKind.DotsHorizontal,"More actions",()=>{moreMenu.PlacementTarget=more;moreMenu.IsOpen=true;});more.ContextMenu=moreMenu;
        MenuItem Item(string label,Action action){var entry=new MenuItem{Header=label,MinHeight=48};entry.Click+=(_,_)=>action();moreMenu.Items.Add(entry);return entry;}
        audioActions.Add(Item("Change language…",()=>{if(Selected is {} entry)controller.ShowInputLanguage(entry.Document);}));audioActions.Add(Item("Save audio…",Export));
        moreMenu.Items.Add(new Separator());Item("Delete…",Delete);
        var actions=new WrapPanel{Margin=new(0,4,0,0)};foreach(var button in new[]{openReview,copyText,more}){button.Margin=new(0,0,8,0);actions.Children.Add(button);}
        Grid.SetRow(actions,1);right.Children.Add(actions);Grid.SetColumn(right,2);body.Children.Add(right);
        root.Children.Add(body);
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        bool? stacked=null;double preferredListWidth=212;
        void ApplyWideWidth()
        {
            if(stacked!=false)return;
            var available=Math.Min(body.ActualWidth,ActualWidth);
            if(available<=0)return;
            // A pixel/star GridSplitter constrains the pixel column itself. Set
            // its maximum as well as the neighbor's minimum before native resizing.
            listColumn.MaxWidth=Math.Max(160,available-dividerColumn.Width.Value-240);
            listColumn.Width=new(Math.Clamp(preferredListWidth,160,listColumn.MaxWidth));
        }
        void RememberWidth()
        {
            if(stacked!=false)return;
            preferredListWidth=listColumn.Width.IsAbsolute?listColumn.Width.Value:listColumn.ActualWidth;ApplyWideWidth();
        }
        splitter.DragCompleted+=(_,args)=>{if(!args.Canceled)RememberWidth();};
        splitter.AddHandler(KeyDownEvent,new KeyEventHandler((_,args)=>{if(args.Key is Key.Left or Key.Right)RememberWidth();}),true);
        body.SizeChanged+=(_,_)=>ApplyWideWidth();
        SizeChanged+=(_,_)=>
        {
            var compact=ActualWidth<600;root.MinHeight=compact?620:420;root.Height=Math.Max(root.MinHeight,ActualHeight);
            if(stacked!=compact)
            {
                stacked=compact;body.MinHeight=compact?390:220;
                listColumn.MinWidth=compact?0:160;listColumn.MaxWidth=double.PositiveInfinity;detailColumn.MinWidth=compact?0:240;
                listColumn.Width=compact?new(1,GridUnitType.Star):new(preferredListWidth);dividerColumn.Width=new(compact?0:10);detailColumn.Width=compact?new(0):new(1,GridUnitType.Star);
                splitter.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
                body.RowDefinitions.Clear();body.RowDefinitions.Add(new(){Height=compact?new(160):new(1,GridUnitType.Star)});
                if(compact)body.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
                items.MinHeight=compact?0:150;listArea.Margin=compact?new(0,0,0,12):new(0);Grid.SetColumn(right,compact?0:2);Grid.SetRow(right,compact?1:0);
            }
            ApplyWideWidth();
        };
        System.Windows.Automation.AutomationProperties.SetName(items,"Recent dictations");System.Windows.Automation.AutomationProperties.SetName(transcript,"Selected dictation transcript");
        items.SelectionChanged+=(_,_)=>{if(!refreshing)Select();};controller.Changed+=Refresh;
    }

    static ControlTemplate DividerTemplate()
    {
        var template=new ControlTemplate(typeof(GridSplitter));var root=new FrameworkElementFactory(typeof(Grid));root.SetValue(Panel.BackgroundProperty,Brushes.Transparent);
        var grip=new FrameworkElementFactory(typeof(Border),"Grip");grip.SetValue(WidthProperty,2d);grip.SetValue(OpacityProperty,0d);grip.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);grip.SetValue(MarginProperty,new Thickness(0,8,0,8));grip.SetValue(Border.CornerRadiusProperty,new CornerRadius(1));grip.SetResourceReference(Border.BackgroundProperty,"InputBorder");root.AppendChild(grip);template.VisualTree=root;
        template.Triggers.Add(new Trigger{Property=IsMouseOverProperty,Value=true,Setters={new Setter(OpacityProperty,1d,"Grip")}});
        template.Triggers.Add(new Trigger{Property=IsKeyboardFocusedProperty,Value=true,Setters={new Setter(OpacityProperty,1d,"Grip"),new Setter(Border.BackgroundProperty,new DynamicResourceExtension("Accent"),"Grip")}});
        return template;
    }

    DataTemplate RowTemplate()
    {
        var template=new DataTemplate(typeof(DictationItem));var row=new FrameworkElementFactory(typeof(StackPanel));
        var dateRow=new FrameworkElementFactory(typeof(DockPanel));
        var duration=new FrameworkElementFactory(typeof(TextBlock));duration.SetBinding(TextBlock.TextProperty,new Binding(nameof(DictationItem.DurationLabel)));duration.SetValue(DockPanel.DockProperty,Dock.Right);duration.SetValue(MarginProperty,new Thickness(8,0,0,0));duration.SetValue(TextBlock.FontSizeProperty,12d);duration.SetValue(StyleProperty,SelectedTextStyle("Muted"));dateRow.AppendChild(duration);
        var date=new FrameworkElementFactory(typeof(TextBlock));date.SetBinding(TextBlock.TextProperty,new Binding("Document.Created"){StringFormat="{0:dd MMM · HH:mm}"});date.SetValue(TextBlock.FontSizeProperty,12d);date.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);date.SetValue(StyleProperty,SelectedTextStyle("Ink"));dateRow.AppendChild(date);row.AppendChild(dateRow);
        var summary=new FrameworkElementFactory(typeof(DockPanel));
        var delete=new FrameworkElementFactory(typeof(Button));delete.SetValue(DockPanel.DockProperty,Dock.Right);
        delete.SetValue(WidthProperty,48d);delete.SetValue(HeightProperty,48d);delete.SetValue(MarginProperty,new Thickness(0));delete.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
        var deleteStyle=new Style(typeof(Button),(Style)FindResource("Md3IconButton"));
        deleteStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Muted")));
        deleteStyle.Triggers.Add(new DataTrigger{Binding=new Binding(nameof(ListBoxItem.IsSelected)){RelativeSource=new(RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)},Value=true,Setters={new Setter(Control.ForegroundProperty,new DynamicResourceExtension("SelectedInk"))}});
        delete.SetValue(StyleProperty,deleteStyle);delete.SetValue(ToolTipProperty,"Delete dictation…");
        delete.SetBinding(AutomationProperties.NameProperty,new Binding("Document.Created"){StringFormat="Delete dictation from {0:g}"});
        delete.AddHandler(LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is Button button&&VisualTreeHelper.GetChildrenCount(button)>0&&VisualTreeHelper.GetChild(button,0) is Panel target)target.Background=Brushes.Transparent;}));
        delete.AddHandler(Button.ClickEvent,new RoutedEventHandler((sender,args)=>{args.Handled=true;if(sender is FrameworkElement{DataContext:DictationItem entry})Delete(entry);}));
        var glyph=new FrameworkElementFactory(typeof(PackIcon));glyph.SetValue(PackIcon.KindProperty,PackIconKind.Close);glyph.SetValue(WidthProperty,16d);glyph.SetValue(HeightProperty,16d);delete.AppendChild(glyph);summary.AppendChild(delete);
        var copy=new FrameworkElementFactory(typeof(StackPanel));summary.AppendChild(copy);row.AppendChild(summary);
        var status=new FrameworkElementFactory(typeof(TextBlock));status.SetBinding(TextBlock.TextProperty,new Binding(nameof(DictationItem.State)));status.SetValue(TextBlock.FontSizeProperty,11d);
        var statusStyle=SelectedTextStyle("Accent");foreach(var quiet in new[]{"Saved","Inserted"})statusStyle.Triggers.Add(new DataTrigger{Binding=new Binding(nameof(DictationItem.State)),Value=quiet,Setters={new Setter(VisibilityProperty,Visibility.Collapsed)}});status.SetValue(StyleProperty,statusStyle);copy.AppendChild(status);
        var preview=new FrameworkElementFactory(typeof(TextBlock));preview.SetBinding(TextBlock.TextProperty,new Binding(nameof(DictationItem.Preview)));preview.SetValue(TextBlock.FontSizeProperty,13d);preview.SetValue(TextBlock.LineHeightProperty,18d);preview.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);preview.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);preview.SetValue(MaxHeightProperty,36d);preview.SetValue(MarginProperty,new Thickness(0,4,0,0));preview.SetValue(StyleProperty,SelectedTextStyle("Muted"));copy.AppendChild(preview);
        template.VisualTree=row;return template;
    }

    static Style SelectedTextStyle(string normalBrush)
    {
        var style=new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty,new DynamicResourceExtension(normalBrush)));
        style.Triggers.Add(new DataTrigger
        {
            Binding=new Binding(nameof(ListBoxItem.IsSelected)){RelativeSource=new(RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)},
            Value=true,Setters={new Setter(TextBlock.ForegroundProperty,new DynamicResourceExtension("SelectedInk"))}
        });
        return style;
    }

    /// <summary>Only the selected main-window section owns decrypted audio.</summary>
    public void SetActive(bool value)
    {
        if(disposed||active==value)return;active=value;
        if(active){shown=null;Refresh();}
        else{moreMenu.IsOpen=false;playback.Clear();loadedAudioId=null;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;active=false;controller.Changed-=Refresh;
        moreMenu.IsOpen=false;playback.Dispose();loadedAudioId=null;
    }
    DictationItem? Selected=>items.SelectedItem as DictationItem;
    void Refresh()
    {
        if(disposed||!active)return;
        var next=controller.Dictations;
        if(shown!=null&&shown.SequenceEqual(next)){UpdateText();return;}
        shown=next;var id=Selected?.Id;refreshing=true;
        try
        {
            items.ItemsSource=next;
            items.SelectedItem=next.FirstOrDefault(e=>e.Id==id)??next.FirstOrDefault();
        }
        finally{refreshing=false;}
        Select();
    }
    void Select()
    {
        moreMenu.IsOpen=false;message.Text="";UpdateText();
        playback.Visibility=Selected?.HasAudio==true?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in audioActions)button.IsEnabled=Selected?.HasAudio==true;
        if(!active||disposed)return;
        if(Selected is {HasAudio:true} current&&current.Id==loadedAudioId)return;
        playback.Clear();loadedAudioId=null;
        if(Selected is not {HasAudio:true} entry)return;
        try{playback.LoadAudio(controller.HistoryAudio(entry.Id));loadedAudioId=entry.Id;}
        catch(Exception){playback.Clear();message.Text="The original recording could not be opened.";}
    }
    void UpdateText()
    {
        var selected=Selected;transcript.Text=selected?.Document.OutputText??"Record a dictation to see its transcript and original audio here.";
        openReview.IsEnabled=copyText.IsEnabled=more.IsEnabled=selected!=null;
        count.Text=$"{shown?.Count??0} items";
        title.Text=selected?.Document.Created.LocalDateTime.ToString("g")??"No dictations yet";
        state.Text=selected?.State??"";
        if(selected==null){detail.Text=language.Text="";return;}
        var model=controller.Models?.FirstOrDefault(m=>m.Id==selected.Document.PrimaryModel);
        detail.Text=model?.Name is {Length:>0} name?name:selected.Document.PrimaryModel.Split('/').Last();
        detail.ToolTip=selected.Document.PrimaryModel;
        language.Text=(selected.HasAudio?"":"Text only · ")+LanguagePolicy.Label(selected.Document.RequestedLanguage,selected.Document.Language,selected.Document.LanguageMode,null);
    }
    void Copy(){if(Selected is not {} entry)return;try{Clipboard.SetText(entry.Document.OutputText);message.Text="Copied.";}catch(Exception){message.Text="Clipboard unavailable. Select and copy the text manually.";}}
    void OpenReview(){if(Selected is {} entry)controller.OpenReview(entry.Document);}
    void Export()
    {
        if(Selected is not {} entry)return;
        var picker=new Microsoft.Win32.SaveFileDialog{Title="Save original recording",Filter="WAV audio|*.wav",FileName=$"dictation-{entry.Document.Created:yyyyMMdd-HHmmss}.wav"};
        var owner=Window.GetWindow(this);
        if((owner==null?picker.ShowDialog():picker.ShowDialog(owner))!=true)return;
        byte[]? bytes=null;
        try{bytes=controller.HistoryAudio(entry.Id);File.WriteAllBytes(picker.FileName,bytes);message.Text="Audio exported to the selected file.";}
        catch(Exception){message.Text="Could not export this recording.";}
        finally{if(bytes!=null)CryptographicOperations.ZeroMemory(bytes);}
    }
    void Delete(){if(Selected is {} entry)Delete(entry);}
    bool ConfirmDelete(DictationItem entry)
    {
        if(!controller.SetShortcutCapture(true)){message.Text="Finish the current operation before deleting this dictation.";return false;}
        try{return showDeleteConfirmation(Window.GetWindow(this),entry);}
        finally{controller.SetShortcutCapture(false);}
    }
    void Delete(DictationItem entry)
    {
        // A modal confirmation can pump selection/refresh events. Keep the clicked
        // identity, and let the controller recheck its operation guards afterwards.
        if(disposed||!active||!confirmDelete(entry)||disposed||!active)return;
        try{controller.DeleteDictation(entry.Id);message.Text="Deleted.";}
        catch(InvalidOperationException error){message.Text=error.Message;}
        catch(Exception){message.Text="Could not delete this history item.";}
    }
}
