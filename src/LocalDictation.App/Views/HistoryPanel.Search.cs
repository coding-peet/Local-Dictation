using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

public sealed partial class HistoryPanel
{
    readonly TextBox historySearch=new(){MinWidth=140,Height=48,Padding=new(16,8,16,8),Margin=new(0)};
    HistoryQuery query=new();
    bool updatingSearch;
    FrameworkElement SearchToolbar()
    {
        var panel=new StackPanel();
        HintAssist.SetHint(historySearch,"Search dictations");HintAssist.SetIsFloating(historySearch,false);AutomationProperties.SetName(historySearch,"Search dictations");
        var importDetails=ImportToolbar();
        filterButton=Ui.Button("Filters",ToggleFilters);filterButton.SetResourceReference(StyleProperty,"Md3OutlinedButton");
        filterButton.Margin=new(8,0,4,0);filterButton.ToolTip="Filter by date, model or status";
        var caption=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        caption.Children.Add(new PackIcon{Kind=PackIconKind.Tune,Width=18,Height=18,Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center});
        filterButtonText.Margin=new(0);filterButtonText.VerticalAlignment=VerticalAlignment.Center;caption.Children.Add(filterButtonText);filterButton.Content=caption;
        var searchRow=new Grid{Margin=new(0,0,0,12)};
        searchRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});searchRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});searchRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        searchRow.Children.Add(historySearch);Grid.SetColumn(filterButton,1);searchRow.Children.Add(filterButton);Grid.SetColumn(importAudio,2);searchRow.Children.Add(importAudio);
        panel.Children.Add(searchRow);
        activeFilterRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});activeFilterRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var chipScroll=new ScrollViewer{Content=activeFilterChips,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=58};
        activeFilterRow.Children.Add(chipScroll);
        var clear=Ui.Button("Clear filters",ClearFilters);Grid.SetColumn(clear,1);activeFilterRow.Children.Add(clear);
        panel.Children.Add(activeFilterRow);panel.Children.Add(importDetails);
        BuildFilterPopup();RefreshAppliedFilters();
        historySearch.TextChanged+=(_,_)=>
        {
            if(updatingSearch)return;
            query=query with {Text=historySearch.Text};Refresh();
        };
        return panel;
    }
    void RefreshSearchModels(IReadOnlyList<DictationItem> all)
    {
        var ids=all.SelectMany(i=>new[]{i.Document.PrimaryModel,i.Document.VerificationModel}).Append(query.Model).Where(id=>!string.IsNullOrWhiteSpace(id)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id).ToArray();
        var current=historyModel.Items.Cast<KeyValuePair<string,string>>().Skip(1).Select(p=>p.Key);
        if(historyModel.Items.Count>0&&current.SequenceEqual(ids))return;
        updatingSearch=true;
        try
        {
            var selected=historyModel.SelectedValue as string??query.Model??"";
            historyModel.ItemsSource=new[]{new KeyValuePair<string,string>("","All models")}.Concat(ids.Select(id=>new KeyValuePair<string,string>(id,controller.Models?.FirstOrDefault(m=>m.Id==id)?.Name??id))).ToArray();
            historyModel.SelectedValue=selected;
            if(historyModel.SelectedIndex<0)historyModel.SelectedValue="";
        }
        finally{updatingSearch=false;}
    }
}
