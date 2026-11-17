using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App.Views;
using LocalDictation.App.Services;
using LocalDictation.Core;

public static class HistoryFilterPopoverChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static object? Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Fields)!.Invoke(target,args);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
    {if(root is T found)yield return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void Layout(FrameworkElement root,int width,int height=820){root.Measure(new(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);}
    static void Click(DependencyObject root,string name)=>Find<Button>(root).Single(button=>AutomationProperties.GetName(button)==name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    static void Save(FrameworkElement root,int width,int height,string path)
    {var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);png.Save(file);}
    sealed class DetachedSource:PresentationSource
    {public override Visual RootVisual{get=>null!;set{}}public override bool IsDisposed=>false;protected override CompositionTarget GetCompositionTargetCore()=>null!;}
    public static void Run(string output,List<string> checks)
    {
        var original=ThemeManager.Choice;
        try
        {
            foreach(var theme in new[]{"Light","Dark"})Check(theme,output,checks);
            CheckDateCultures(output,checks);
        }
        finally{ThemeManager.Apply(original);}
    }
    static void CheckDateCultures(string output,List<string> checks)
    {
        var original=CultureInfo.CurrentCulture;
        try
        {
            foreach(var (culture,text) in new[]{("sk-SK","17.11.2026"),("en-US","11/17/2026"),("en-GB","17/11/2026")})
            {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
                var doc=new ReviewDocument();doc.SetPrimary("Synthetic date culture fixture.");
                var owner=ReviewWorkflowChecks.Controller(output,doc);Set(owner,"<History>k__BackingField",new List<HistoryEntry>{new(doc,0){HasAudio=false}});
                using var panel=new HistoryPanel(owner);panel.SetActive(true);Layout(panel,760);Call(panel,"PrepareFilterDraft");
                var surface=Get<Border>(panel,"filterSurface");surface.Measure(new(400,double.PositiveInfinity));surface.Arrange(new Rect(0,0,400,surface.DesiredSize.Height));surface.UpdateLayout();SettingsHelpChecks.SettleForRender(surface);
                var date=Get<DatePicker>(panel,"fromDate");var raw=(TextBox)date.Template.FindName("PART_TextBox",date)!;raw.Text=text;
                Require((bool)Call(panel,"ApplyFilterDraft")!&&Get<HistoryQuery>(panel,"query").From==new DateOnly(2026,11,17),"Date filter did not apply the visible date using its configured culture: "+culture);
                Call(panel,"PrepareFilterDraft");Require((bool)Call(panel,"ApplyFilterDraft")!&&Get<HistoryQuery>(panel,"query").From==new DateOnly(2026,11,17),"Reopening a date filter changed its day/culture: "+culture);
            }
        }
        finally{CultureInfo.CurrentCulture=original;}
        checks.Add("Visible date input and reopened filter round-trip correctly in Slovak, US and GB cultures without native input");
    }
    static void Check(string theme,string output,List<string> checks)
    {
        ThemeManager.Apply(theme);
        var first=new ReviewDocument{Created=DateTimeOffset.Now,PrimaryModel="alpha-model",InsertedAt=DateTimeOffset.Now};first.SetPrimary("Keep alpha selected transcript.");
        var second=new ReviewDocument{Created=first.Created.AddDays(-1),PrimaryModel="beta-model"};second.SetPrimary("Keep beta text-only transcript.");
        var third=new ReviewDocument{Created=first.Created.AddDays(-2),PrimaryModel="other-model"};third.SetPrimary("Other unrelated words.");
        var store=new HistoryStore(Path.Combine(output,"filter-fixture-"+Guid.NewGuid().ToString("N")));foreach(var doc in new[]{first,second,third})store.Add(doc,new byte[32000]);
        var owner=ReviewWorkflowChecks.Controller(output,first);Set(owner,"historyStore",store);Set(owner,"<History>k__BackingField",new List<HistoryEntry>{new(first,1),new(second,1){HasAudio=false},new(third,1){HasAudio=false}});
        var sourceText=string.Join("|",new[]{first.Primary,second.Primary,third.Primary});
        using var panel=new HistoryPanel(owner);panel.SetActive(true);Layout(panel,760);
        var list=Get<ListBox>(panel,"items");var search=Get<TextBox>(panel,"historySearch");
        var button=Find<Button>(panel).SingleOrDefault(item=>AutomationProperties.GetName(item)=="Filters");
        Require(button!=null,"Inactive filters need a compact Filters button in the existing search row, not a permanent card.");
        var row=Get<Grid>(panel,"activeFilterRow");var chips=Get<StackPanel>(panel,"activeFilterChips");var surface=Get<Border>(panel,"filterSurface");var popup=Get<Popup>(panel,"filtersPopup");
        surface.Measure(new(400,double.PositiveInfinity));surface.Arrange(new Rect(0,0,400,surface.DesiredSize.Height));surface.UpdateLayout();SettingsHelpChecks.SettleForRender(surface);
        Require(row.Visibility==Visibility.Collapsed&&chips.Children.Count==0&&!Find<Expander>(panel).Any(),"Inactive filters still reserve a card or chip row.");
        search.Text="KEEP";Require(list.Items.Count==2&&(list.SelectedItem as DictationItem)?.Id==first.Id,"Immediate search lost text matching or the selected identity.");
        Call(panel,"PrepareFilterDraft");Get<CheckBox>(panel,"textOnly").IsChecked=true;Get<ComboBox>(panel,"historyModel").SelectedValue="beta-model";
        Require(list.Items.Count==2&&row.Visibility==Visibility.Collapsed,"Editing a filter draft immediately changed results or displayed unapplied chips.");
        Require((bool)Call(panel,"ApplyFilterDraft")!&&list.Items.Count==1&&list.SelectedItem==null,"Apply did not commit both conditions or auto-selected another dictation.");
        Require(chips.Children.Count==2&&Get<TextBlock>(panel,"filterButtonText").Text=="Filters · 2","Applied chips/count disagree with the restrictions.");
        Click(chips,"Remove filter: Model: beta-model");Require(list.Items.Count==1&&chips.Children.Count==1&&search.Text=="KEEP","Removing model chip changed another restriction or search text.");
        Call(panel,"ClearFilters");Require(list.Items.Count==2&&search.Text=="KEEP"&&(list.SelectedItem as DictationItem)?.Id==first.Id&&row.Visibility==Visibility.Collapsed,"Clear filters cleared search, lost selection or retained an empty row.");
        checks.Add("Applied-only chips, independent chip removal, clear preserving text and stable selected identity "+theme);

        Call(panel,"PrepareFilterDraft");Get<CheckBox>(panel,"inserted").IsChecked=true;Click(surface,"Cancel");
        Require(!Get<CheckBox>(panel,"inserted").IsChecked!.Value&&list.Items.Count==2&&row.Visibility==Visibility.Collapsed,"Cancel retained a draft or changed filtering.");
        Call(panel,"PrepareFilterDraft");Get<CheckBox>(panel,"needsReview").IsChecked=true;
        var escape=new KeyEventArgs(Keyboard.PrimaryDevice,new DetachedSource(),0,Key.Escape){RoutedEvent=UIElement.PreviewKeyDownEvent};surface.RaiseEvent(escape);
        Require(escape.Handled&&Get<CheckBox>(panel,"needsReview").IsChecked==false&&list.Items.Count==2,"Escape did not discard the current draft.");
        Call(panel,"PrepareFilterDraft");Get<DatePicker>(panel,"fromDate").SelectedDate=DateTime.Today.AddDays(1);Get<DatePicker>(panel,"toDate").SelectedDate=DateTime.Today;
        Require(!(bool)Call(panel,"ApplyFilterDraft")!&&list.Items.Count==2&&Get<TextBlock>(panel,"filterError").Visibility==Visibility.Visible&&row.Visibility==Visibility.Collapsed,"An invalid range changed applied filtering or hid its error.");
        search.Text="beta";Require(list.Items.Count==1,"An invalid popup draft blocks independent text search.");Click(surface,"Cancel");
        Require(Get<DatePicker>(panel,"fromDate").SelectedDate==null&&search.Text=="beta","Closing an invalid draft changed the applied query or kept its hidden invalid date.");
        search.Text="KEEP";Call(panel,"PrepareFilterDraft");Get<DatePicker>(panel,"fromDate").Text=first.Created.LocalDateTime.ToString("d");Get<DatePicker>(panel,"toDate").Text=first.Created.LocalDateTime.AddDays(1).ToString("d");
        Require((bool)Call(panel,"ApplyFilterDraft")!&&list.Items.Count==1&&chips.Children.Count==1,"Valid typed date range failed or produced two chips for one range.");
        Call(panel,"RemoveFilter","dates");Require(list.Items.Count==2&&chips.Children.Count==0,"Date chip failed to remove both range bounds.");
        Call(panel,"ClearFilters");Call(panel,"PrepareFilterDraft");Get<CheckBox>(panel,"inserted").IsChecked=true;
        var applied=Get<HistoryQuery>(panel,"query");var fromPicker=Get<DatePicker>(panel,"fromDate");
        fromPicker.ApplyTemplate();var rawDate=(TextBox)fromPicker.Template.FindName("PART_TextBox",fromPicker)!;
        rawDate.Text="not a date";rawDate.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Require(!(bool)Call(panel,"ApplyFilterDraft")!&&Get<HistoryQuery>(panel,"query")==applied&&Get<TextBlock>(panel,"filterError").Visibility==Visibility.Visible,"A DatePicker-reverted invalid date silently applied other draft filters.");
        fromPicker.SelectedDate=first.Created.LocalDateTime.Date;
        Require((bool)Call(panel,"ApplyFilterDraft")!,"A corrected calendar date did not clear its earlier validation error.");
        Call(panel,"ClearFilters");Call(panel,"PrepareFilterDraft");applied=Get<HistoryQuery>(panel,"query");rawDate.Text="still not a date";
        Require(!(bool)Call(panel,"ApplyFilterDraft")!&&Get<HistoryQuery>(panel,"query")==applied,"Enter-style apply ignored uncommitted invalid raw date text.");
        rawDate.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,new TextComposition(InputManager.Current,rawDate,"1")){RoutedEvent=TextCompositionManager.PreviewTextInputEvent});
        rawDate.Text=first.Created.LocalDateTime.ToString("d");
        Require((bool)Call(panel,"ApplyFilterDraft")!,"An explicit valid keyboard correction could not recover from an invalid raw date.");
        Call(panel,"ClearFilters");Call(panel,"PrepareFilterDraft");rawDate.Text="invalid again";rawDate.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Click(surface,"Cancel");
        Call(panel,"PrepareFilterDraft");Require((bool)Call(panel,"ApplyFilterDraft")!,"Cancel kept a stale invalid-date state for the next filter session.");
        checks.Add("Draft Apply/Cancel/Escape, invalid/reverted/raw dates retaining applied query, independent search and one range chip "+theme);

        foreach(var width in new[]{432,640,1040})
        {
            Call(panel,"ClearFilters");Layout(panel,width);
            var searchBounds=search.TransformToAncestor(panel).TransformBounds(new Rect(search.RenderSize));var triggerBounds=button!.TransformToAncestor(panel).TransformBounds(new Rect(button.RenderSize));var import=Get<Button>(panel,"importAudio");var importBounds=import.TransformToAncestor(panel).TransformBounds(new Rect(import.RenderSize));
            Require(Math.Abs(searchBounds.Top-triggerBounds.Top)<1&&searchBounds.Right<=triggerBounds.Left&&triggerBounds.Right<=importBounds.Left,"Compact filter trigger overlaps search/import or reserves another row.");
            Require(search.ActualWidth>=140&&button.ActualHeight>=48&&row.ActualHeight==0,"Inactive layout lost usable input/target or retains empty chip space.");
            Save(panel,width,820,Path.Combine(output,$"history-filter-inactive-{width}-{theme}.png"));
            Call(panel,"PrepareFilterDraft");Get<CheckBox>(panel,"textOnly").IsChecked=true;Get<ComboBox>(panel,"historyModel").SelectedValue="beta-model";Call(panel,"ApplyFilterDraft");Layout(panel,width);
            Require(row.Visibility==Visibility.Visible&&row.ActualHeight<=66,"Active chip summary grows into a wrapping block.");
            Save(panel,width,820,Path.Combine(output,$"history-filter-active-{width}-{theme}.png"));
            var heightBefore=panel.DesiredSize.Height;Call(panel,"PrepareFilterDraft");surface.Width=Math.Min(400,width);surface.Measure(new(surface.Width,double.PositiveInfinity));surface.Arrange(new Rect(0,0,surface.Width,surface.DesiredSize.Height));surface.UpdateLayout();SettingsHelpChecks.SettleForRender(surface);Layout(panel,width);
            Require(ReferenceEquals(popup.Child,surface)&&!surface.IsDescendantOf(panel)&&panel.DesiredSize.Height==heightBefore,"Filter form still consumes normal History layout space.");
            var placement=popup.CustomPopupPlacementCallback!(surface.RenderSize,button.RenderSize,new Point())[0];var x=button.TranslatePoint(new Point(),panel).X+placement.Point.X;
            Require(x>=-.1&&x+surface.ActualWidth<=panel.ActualWidth+.1,"Popover placement escapes compact History width.");
            Save(surface,(int)surface.Width,(int)Math.Ceiling(surface.ActualHeight),Path.Combine(output,$"history-filter-popup-{width}-{theme}.png"));
            Require(Find<Button>(surface).Where(action=>AutomationProperties.GetName(action) is "Clear filters" or "Cancel" or "Apply").All(action=>action.ActualHeight>=48),"Filter popup actions lost their comfortable interaction targets.");
        }
        Require(!popup.IsOpen&&PresentationSource.FromVisual(panel)==null&&PresentationSource.FromVisual(surface)==null,"Filter verification created a native popup or window.");
        var audio=Get<HistoryAudioPlayback>(Get<HistoryAudioPlayerControl>(panel,"playback"),"playback");Require(Get<object?>(audio,"output")==null,"Filter verification opened audio output.");
        panel.SetActive(false);panel.Dispose();Require(!popup.IsOpen,"Navigation/disposal left a filter popup alive.");
        Require(string.Join("|",new[]{first.Primary,second.Primary,third.Primary})==sourceText,"Filtering modified transcript source text.");
        checks.Add("Compact/wide inactive/active and detached popup Light/Dark layouts without native window, input or audio "+theme);
    }
}
