using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

public sealed partial class HistoryPanel
{
    static DatePicker FilterDatePicker()=>new(){MinWidth=120,Language=XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag is {Length:>0} tag?tag:"en-US")};
    readonly DatePicker fromDate=FilterDatePicker(),toDate=FilterDatePicker();
    readonly HashSet<DatePicker> invalidFilterDates=[];
    readonly ComboBox historyModel=new(){DisplayMemberPath="Value",SelectedValuePath="Key"};
    readonly CheckBox needsReview=new(){Content="Needs review"},textOnly=new(){Content="Text only"},inserted=new(){Content="Inserted"};
    readonly TextBlock filterError=Ui.Text("",12),filterButtonText=Ui.Text("Filters",13);
    readonly Grid activeFilterRow=new(){Visibility=Visibility.Collapsed,Margin=new(0,0,0,8)};
    readonly StackPanel activeFilterChips=new(){Orientation=Orientation.Horizontal};
    readonly Popup filtersPopup=new(){AllowsTransparency=true,StaysOpen=false,Placement=PlacementMode.Custom,PopupAnimation=PopupAnimation.Fade};
    Border filterSurface=null!;
    Button filterButton=null!;
    HistoryQuery? renderedFilters;
    string? renderedFilterModel;

    void BuildFilterPopup()
    {
        var form=new StackPanel();form.Children.Add(Ui.Text("Filter history",18,true));
        var dates=new Grid();dates.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});dates.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var first=new StackPanel{Margin=new(0,0,12,0)};Ui.Label(first,"From",fromDate);dates.Children.Add(first);
        var last=new StackPanel();Ui.Label(last,"To",toDate);Grid.SetColumn(last,1);dates.Children.Add(last);form.Children.Add(dates);
        Ui.Label(form,"Model",historyModel);
        var states=new WrapPanel();
        foreach(var check in new[]{needsReview,textOnly,inserted})
        {
            check.MinHeight=48;check.Margin=new(0,0,12,0);check.SetResourceReference(Control.ForegroundProperty,"Ink");
            AutomationProperties.SetName(check,check.Content.ToString());states.Children.Add(check);
        }
        form.Children.Add(states);
        filterError.SetResourceReference(TextBlock.ForegroundProperty,"Warning");filterError.Visibility=Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(filterError,AutomationLiveSetting.Polite);form.Children.Add(filterError);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(Ui.Button("Clear filters",ClearFilters));actions.Children.Add(Ui.Button("Cancel",()=>CloseFilters(true)));actions.Children.Add(Ui.Button("Apply",()=>ApplyFilterDraft(),true));form.Children.Add(actions);
        filterSurface=new Border{Child=new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=480},Width=400,Padding=new(16),BorderThickness=new(1)};
        filterSurface.SetResourceReference(Border.BackgroundProperty,"SurfaceContainerHigh");filterSurface.SetResourceReference(Border.BorderBrushProperty,"InputBorder");filterSurface.SetResourceReference(Border.CornerRadiusProperty,"ShapeMedium");
        filterSurface.SetResourceReference(TextElement.FontFamilyProperty,"MaterialDesignFont");filterSurface.SetValue(TextElement.FontSizeProperty,14d);filterSurface.SetResourceReference(TextElement.ForegroundProperty,"Ink");
        AutomationProperties.SetName(filterSurface,"History filters");KeyboardNavigation.SetTabNavigation(filterSurface,KeyboardNavigationMode.Cycle);
        filterSurface.PreviewKeyDown+=(_,args)=>
        {
            if(args.Key==Key.Escape){CloseFilters(true);args.Handled=true;}
            else if(args.Key==Key.Enter&&Keyboard.FocusedElement is not ButtonBase&&!historyModel.IsDropDownOpen&&!fromDate.IsDropDownOpen&&!toDate.IsDropDownOpen)
            {ApplyFilterDraft();args.Handled=true;}
        };
        foreach(var date in new[]{fromDate,toDate})
        {
            date.SetResourceReference(Control.ForegroundProperty,"Ink");
            date.DateValidationError+=(_,args)=>
            {
                args.ThrowException=false;
                if(updatingSearch)return;
                // DatePicker restores the previous text on focus loss. Retain
                // the failed attempt until an explicit edit/reselection or cancel.
                invalidFilterDates.Add(date);ShowFilterError("Enter or select a valid date before applying. The applied filters have not changed.");
            };
            date.SelectedDateChanged+=(_,_)=>{if(!updatingSearch)ClearDateError(date);};
            date.PreviewTextInput+=(_,_)=>ClearDateError(date);
            date.PreviewKeyDown+=(_,args)=>
            {
                if(args.Key is Key.Back or Key.Delete||(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&args.Key is Key.V or Key.X))ClearDateError(date);
            };
            DataObject.AddPastingHandler(date,(_,_)=>ClearDateError(date));
            date.CalendarClosed+=(_,_)=>{if(date.SelectedDate!=null&&ReadFilterDate(date,out _))ClearDateError(date);};
        }
        filtersPopup.Child=filterSurface;filtersPopup.PlacementTarget=filterButton;
        filtersPopup.CustomPopupPlacementCallback=(popupSize,targetSize,offset)=>
        {
            var triggerX=filterButton.TranslatePoint(new Point(),this).X;
            var min=-triggerX;var max=ActualWidth-triggerX-popupSize.Width;
            var x=max>=min?Math.Clamp(targetSize.Width-popupSize.Width,min,max):min;
            return [new(new(x,targetSize.Height+4),PopupPrimaryAxis.Horizontal),new(new(x,-popupSize.Height-4),PopupPrimaryAxis.Horizontal)];
        };
        filtersPopup.Closed+=(_,_)=>PrepareFilterDraft();
        filtersPopup.Opened+=(_,_)=>Dispatcher.BeginInvoke(DispatcherPriority.Loaded,(Action)(()=>{if(filtersPopup.IsOpen&&active&&!disposed)fromDate.Focus();}));
        filterButton.Unloaded+=(_,_)=>CloseFilters(false);
    }
    void ToggleFilters()
    {
        if(disposed||!active||PresentationSource.FromVisual(filterButton)==null)return;
        if(filtersPopup.IsOpen){CloseFilters(true);return;}
        PrepareFilterDraft();filterSurface.Width=Math.Clamp(ActualWidth,320,400);filtersPopup.IsOpen=true;
    }
    void PrepareFilterDraft()
    {
        updatingSearch=true;invalidFilterDates.Clear();
        try
        {
            fromDate.SelectedDate=query.From?.ToDateTime(TimeOnly.MinValue);toDate.SelectedDate=query.To?.ToDateTime(TimeOnly.MinValue);
            fromDate.Text=fromDate.SelectedDate?.ToString("d",CultureInfo.CurrentCulture)??"";toDate.Text=toDate.SelectedDate?.ToString("d",CultureInfo.CurrentCulture)??"";
            historyModel.SelectedValue=query.Model??"";needsReview.IsChecked=query.NeedsReview;textOnly.IsChecked=query.TextOnly;inserted.IsChecked=query.Inserted;
            filterError.Text="";filterError.Visibility=Visibility.Collapsed;
        }
        finally{updatingSearch=false;}
    }
    static bool ReadFilterDate(DatePicker control,out DateOnly? value)
    {
        // Enter can reach Apply before DatePicker commits its inner textbox.
        var text=control.Template?.FindName("PART_TextBox",control) is TextBox box?box.Text:control.Text;
        value=null;if(string.IsNullOrWhiteSpace(text))return true;
        if(!DateTime.TryParse(text,control.Language.GetSpecificCulture(),DateTimeStyles.AllowWhiteSpaces,out var parsed))return false;
        value=DateOnly.FromDateTime(parsed);return true;
    }
    void ClearDateError(DatePicker date)
    {
        if(invalidFilterDates.Remove(date)&&invalidFilterDates.Count==0){filterError.Text="";filterError.Visibility=Visibility.Collapsed;}
    }
    void ShowFilterError(string text){filterError.Text=text;filterError.Visibility=Visibility.Visible;}
    bool ApplyFilterDraft()
    {
        var validFrom=ReadFilterDate(fromDate,out var from);var validTo=ReadFilterDate(toDate,out var to);
        if(!validFrom)invalidFilterDates.Add(fromDate);if(!validTo)invalidFilterDates.Add(toDate);
        if(invalidFilterDates.Count>0)
        {ShowFilterError("Enter or select valid From and To dates before applying. The applied filters have not changed.");return false;}
        if(from>to){ShowFilterError("From must be on or before To. The applied filters have not changed.");return false;}
        query=new(historySearch.Text,from,to,historyModel.SelectedValue as string,needsReview.IsChecked==true,textOnly.IsChecked==true,inserted.IsChecked==true);
        RefreshAppliedFilters();Refresh();CloseFilters(true);return true;
    }
    void CloseFilters(bool restoreFocus)
    {
        filtersPopup.IsOpen=false;PrepareFilterDraft();
        if(restoreFocus&&active&&!disposed&&PresentationSource.FromVisual(filterButton)!=null)filterButton.Focus();
    }
    void ClearFilters()
    {
        query=new(historySearch.Text);RefreshAppliedFilters();Refresh();CloseFilters(true);
    }
    void RemoveFilter(string id)
    {
        query=id switch
        {
            "dates"=>query with{From=null,To=null},"model"=>query with{Model=null},
            "review"=>query with{NeedsReview=false},"text"=>query with{TextOnly=false},"inserted"=>query with{Inserted=false},_=>query
        };
        RefreshAppliedFilters();Refresh();PrepareFilterDraft();
        if(active&&PresentationSource.FromVisual(filterButton)!=null)filterButton.Focus();
    }
    void RefreshAppliedFilters()
    {
        var restrictions=query with{Text=""};
        var modelName=string.IsNullOrEmpty(query.Model)?null:controller.Models?.FirstOrDefault(model=>model.Id==query.Model)?.Name??query.Model;
        if(restrictions==renderedFilters&&modelName==renderedFilterModel)return;
        renderedFilters=restrictions;renderedFilterModel=modelName;activeFilterChips.Children.Clear();
        void Add(string id,string label)
        {
            var chip=Ui.Button(label,()=>RemoveFilter(id));chip.SetResourceReference(StyleProperty,"Md3OutlinedButton");chip.Margin=new(0,0,4,0);chip.MaxWidth=240;chip.ToolTip=label;
            var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            content.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});
            var close=new PackIcon{Kind=PackIconKind.Close,Width=14,Height=14,Margin=new(6,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(close,1);content.Children.Add(close);chip.Content=content;
            AutomationProperties.SetName(chip,"Remove filter: "+label);activeFilterChips.Children.Add(chip);
        }
        if(query.From!=null||query.To!=null)Add("dates",query.From!=null&&query.To!=null?$"Dates: {query.From:d} – {query.To:d}":query.From!=null?$"From: {query.From:d}":$"To: {query.To:d}");
        if(modelName!=null)Add("model","Model: "+modelName);
        if(query.NeedsReview)Add("review","Needs review");if(query.TextOnly)Add("text","Text only");if(query.Inserted)Add("inserted","Inserted");
        var count=activeFilterChips.Children.Count;activeFilterRow.Visibility=count==0?Visibility.Collapsed:Visibility.Visible;
        filterButtonText.Text=count==0?"Filters":$"Filters · {count}";
        AutomationProperties.SetHelpText(filterButton,count==0?"Open history filters. No filters applied.":$"Open history filters. {count} applied.");
    }
}
