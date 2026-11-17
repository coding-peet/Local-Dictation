using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace LocalDictation.App.Views;
public sealed partial class SettingsWindow
{
    readonly ToggleButton privateNext=Ui.Switch("Private for next microphone recording");
    readonly TextBlock privateStatus=Ui.Text("",12);
    PrivateTranscriptPicker privatePicker=null!;
    FrameworkElement PrivacySection()
    {
        var panel=new StackPanel();
        panel.Children.Add(Ui.SwitchRow("Private for next microphone recording",privateNext,
            "Keep the next microphone recording and its text out of History and saved Review. Text stays only in memory until discarded or the app exits. This does not erase records made by Windows, clipboard history or the destination app. Audio imports remain saved to History. Automatic incognito activation is not available."));
        privatePicker=new PrivateTranscriptPicker(controller.OpenPrivateReview);panel.Children.Add(privatePicker);panel.Children.Add(privateStatus);
        privateNext.Checked+=(_,_)=>ChangePrivate();privateNext.Unchecked+=(_,_)=>ChangePrivate();return panel;
    }
    void ChangePrivate()
    {
        if(loading||updating)return;
        if(!controller.SetPrivateNextRecording(privateNext.IsChecked==true))
        {privateNext.IsChecked=controller.PrivateNextRecording;message.Text="Finish the current operation before changing private recording.";}
    }
    void RefreshPrivate()
    {
        privateNext.IsChecked=controller.PrivateNextRecording;privateNext.IsEnabled=!controller.IsBusy&&!controller.IsApplyingReview;
        var choices=controller.PrivateReviewChoices;var count=choices.Count;privatePicker.Refresh(choices,controller.CanOpenPrivateReviews);
        privateStatus.Text=count>0?$"{count} private transcript(s) in memory. They are lost when the app exits.":controller.PrivateNextRecording?"Next microphone recording: private. Imports are unavailable until this choice is turned off.":"";
        privateStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");privateStatus.Visibility=privateStatus.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
}
