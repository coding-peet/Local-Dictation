using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

public sealed partial class HistoryPanel
{
    readonly TextBox historySearch=new(){MinWidth=140,Height=48,Padding=new(16,8,16,8),Margin=new(0)};
    readonly DatePicker fromDate=new(){Width=140,Margin=new(0,0,12,8)},toDate=new(){Width=140,Margin=new(0,0,12,8)};
    readonly ComboBox historyModel=new(){MinWidth=170,MaxWidth=280,DisplayMemberPath="Value",SelectedValuePath="Key",Margin=new(0,0,12,8)};
    readonly CheckBox needsReview=new(){Content="Needs review",Margin=new(0,0,12,8)},textOnly=new(){Content="Text only",Margin=new(0,0,12,8)},inserted=new(){Content="Inserted",Margin=new(0,0,12,8)};
    readonly Expander historyFilters=new(){MinHeight=48,Height=48};
    readonly TextBlock filterError=Ui.Text("",12);
    HistoryQuery query=new();
    bool updatingSearch;
    FrameworkElement SearchToolbar()
    {
        var panel=new StackPanel();
        historyFilters.Header=new TextBlock{Text="Filters",TextWrapping=TextWrapping.NoWrap,VerticalAlignment=VerticalAlignment.Center};
        HintAssist.SetHint(historySearch,"Search dictations");HintAssist.SetIsFloating(historySearch,false);AutomationProperties.SetName(historySearch,"Search dictations");
        var importDetails=ImportToolbar();var searchRow=new DockPanel{Margin=new(0,0,0,12)};
        DockPanel.SetDock(importAudio,Dock.Right);searchRow.Children.Add(importAudio);searchRow.Children.Add(historySearch);
        panel.Children.Add(searchRow);panel.Children.Add(importDetails);
        var filterSection=new StackPanel();filterSection.Children.Add(historyFilters);
        var filters=new WrapPanel{Visibility=Visibility.Collapsed};
        void Label(string label,Control control){var p=new StackPanel{Margin=new(0,0,0,0)};p.Children.Add(Ui.Text(label,12));p.Children.Add(control);AutomationProperties.SetName(control,label);filters.Children.Add(p);}
        Label("From",fromDate);Label("To",toDate);Label("Model",historyModel);
        foreach(var c in new[]{needsReview,textOnly,inserted}){c.SetResourceReference(Control.ForegroundProperty,"Ink");c.VerticalAlignment=VerticalAlignment.Bottom;filters.Children.Add(c);c.Checked+=(_,_)=>ApplySearch();c.Unchecked+=(_,_)=>ApplySearch();}
        filters.Children.Add(Ui.Button("Clear filters",()=>
        {
            updatingSearch=true;
            try{historySearch.Clear();fromDate.SelectedDate=toDate.SelectedDate=null;fromDate.Text=toDate.Text="";historyModel.SelectedValue="";needsReview.IsChecked=textOnly.IsChecked=inserted.IsChecked=false;}
            finally{updatingSearch=false;}ApplySearch();
        }));
        historyFilters.SetResourceReference(Control.ForegroundProperty,"Ink");
        historyFilters.SetResourceReference(Control.BackgroundProperty,"SurfaceContainer");
        AutomationProperties.SetName(historyFilters,"History filters");
        historyFilters.Expanded+=(_,_)=>filters.Visibility=Visibility.Visible;historyFilters.Collapsed+=(_,_)=>filters.Visibility=Visibility.Collapsed;filterSection.Children.Add(filters);
        filterError.SetResourceReference(TextBlock.ForegroundProperty,"Warning");filterError.Visibility=Visibility.Collapsed;filterSection.Children.Add(filterError);
        historySearch.TextChanged+=(_,_)=>ApplySearch();historyModel.SelectionChanged+=(_,_)=>ApplySearch();
        foreach(var date in new[]{fromDate,toDate})
        {
            date.SetResourceReference(Control.ForegroundProperty,"Ink");
            date.SelectedDateChanged+=(_,_)=>ApplySearch();date.LostKeyboardFocus+=(_,_)=>ApplySearch();
            date.DateValidationError+=(_,e)=>{e.ThrowException=false;filterError.Text="Enter a valid date. The previous filters remain active.";filterError.Visibility=Visibility.Visible;};
        }
        var card=Ui.Card(filterSection);card.Padding=new(12);card.Margin=new(0,0,0,12);
        AutomationProperties.SetName(card,"History filters block");panel.Children.Add(card);return panel;
    }
    void ApplySearch()
    {
        if(updatingSearch)return;
        if((!string.IsNullOrWhiteSpace(fromDate.Text)&&!DateTime.TryParse(fromDate.Text,out _))||
            (!string.IsNullOrWhiteSpace(toDate.Text)&&!DateTime.TryParse(toDate.Text,out _)))
        {filterError.Text="Enter a valid date. The previous filters remain active.";filterError.Visibility=Visibility.Visible;return;}
        var from=fromDate.SelectedDate is {} f?DateOnly.FromDateTime(f):(DateOnly?)null;
        var to=toDate.SelectedDate is {} t?DateOnly.FromDateTime(t):(DateOnly?)null;
        if(from>to){filterError.Text="From must be on or before To. The previous filters remain active.";filterError.Visibility=Visibility.Visible;return;}
        filterError.Text="";filterError.Visibility=Visibility.Collapsed;
        query=new(historySearch.Text,from,to,historyModel.SelectedValue as string,needsReview.IsChecked==true,textOnly.IsChecked==true,inserted.IsChecked==true);
        Refresh();
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
