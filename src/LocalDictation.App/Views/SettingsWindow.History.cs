using System.Windows;
using System.Windows.Controls;

namespace LocalDictation.App.Views;

public sealed partial class SettingsWindow
{
    readonly ComboBox historyRetention=new(){DisplayMemberPath="Value",SelectedValuePath="Key"};
    readonly TextBlock historyRetentionStatus=Ui.Text("",12);
    readonly List<KeyValuePair<int,string>> retentionChoices=[new(1,"1 day"),new(7,"7 days"),new(30,"30 days"),new(90,"90 days"),new(365,"1 year"),new(0,"Forever")];

    FrameworkElement HistorySection()
    {
        var panel=new StackPanel();panel.Children.Add(Ui.Text("History",18,true));
        historyRetention.ItemsSource=retentionChoices;
        Ui.Label(panel,"Keep recordings for",historyRetention,"Remove recordings after this time. Transcripts, edits and saved Review choices stay until you delete the dictation. Expired recordings cannot be played, exported or transcribed again. Cleanup happens automatically while the app runs or next time you open it. Shortening this period can remove older recordings immediately. Forever keeps recordings until you delete them.");
        var text=Ui.Text("Transcripts stay in History until you delete them.",12);text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(text);
        historyRetentionStatus.SetResourceReference(TextBlock.ForegroundProperty,"Warning");panel.Children.Add(historyRetentionStatus);
        return panel;
    }
    void RefreshHistoryOptions(bool initial)
    {
        if(initial)
        {
            var days=controller.Settings.AudioRetentionDays;
            if(!retentionChoices.Any(choice=>choice.Key==days))
            {retentionChoices.Add(new(days,$"{days} days"));historyRetention.ItemsSource=retentionChoices.ToArray();}
            historyRetention.SelectedValue=days;
        }
        historyRetention.IsEnabled=!controller.IsBusy&&!controller.IsApplyingReview;
        historyRetentionStatus.Text=controller.HistoryRetentionMessage??"";
        historyRetentionStatus.Visibility=string.IsNullOrEmpty(historyRetentionStatus.Text)?Visibility.Collapsed:Visibility.Visible;
    }
}
