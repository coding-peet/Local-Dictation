using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

public sealed partial class SettingsWindow
{
    readonly ToggleButton startWithWindows=Ui.Switch("Start with Windows");
    readonly TextBlock startupStatus=Ui.Text("",12),startupError=Ui.Text("",12);
    readonly Expander serviceAdvanced=new(){Header="Advanced",HorizontalContentAlignment=HorizontalAlignment.Stretch};
    readonly Expander dictationAdvanced=new(){Header="Advanced",HorizontalContentAlignment=HorizontalAlignment.Stretch};
    readonly TextBlock dictationAdvancedSummary=Ui.Text("",12);
    bool refreshingStartup;

    static void ConfigureAdvanced(Expander expander)
    {
        var heading=Ui.Text("Advanced",18,true);heading.Margin=new(0);
        expander.Header=heading;expander.Padding=new(0);
        expander.SetResourceReference(Control.BackgroundProperty,"SurfaceContainer");
        expander.SetResourceReference(Control.ForegroundProperty,"Ink");
        ExpanderAssist.SetExpanderButtonPosition(expander,ExpanderButtonPosition.Start);
        ExpanderAssist.SetHorizontalHeaderPadding(expander,new(0,8,0,8));
    }
    UIElement GeneralPage()
    {
        ConfigureAdvanced(dictationAdvanced);
        var page=new StackPanel{Margin=new(0,8,8,0)};
        var startup=new StackPanel();startup.Children.Add(Ui.Text("Startup",18,true));
        startup.Children.Add(Ui.SwitchRow("Start with Windows",startWithWindows,
            "Start Local Dictation in the system tray when you sign in to Windows. No recording starts automatically. Windows Startup apps settings can override registration. Turning this off does not stop the running app or local APIs."));
        startupStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");startup.Children.Add(startupStatus);
        startupError.SetResourceReference(TextBlock.ForegroundProperty,"Warning");startup.Children.Add(startupError);
        startWithWindows.Checked+=(_,_)=>ChangeStartup();startWithWindows.Unchecked+=(_,_)=>ChangeStartup();
        page.Children.Add(Section(startup));
        var appearance=new StackPanel();appearance.Children.Add(Ui.Text("Appearance",18,true));
        Ui.Label(appearance,"Color theme",theme,"System follows Windows. Light and Dark override it.");
        page.Children.Add(Section(appearance));
        page.Children.Add(Section(ShortcutSection()));page.Children.Add(Section(HistorySection()));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxWidth=880,HorizontalAlignment=HorizontalAlignment.Stretch};
    }

    FrameworkElement DictationAdvancedSection()
    {
        var body=new StackPanel();body.Children.Add(Ui.SettingsGroup((StackPanel)DictionarySection()));
        var compatibilitySection=CompatibilitySection();compatibilitySection.Margin=new(0,20,0,0);body.Children.Add(Ui.SettingsGroup(compatibilitySection));
        dictationAdvanced.Content=body;
        dictationAdvanced.Expanded+=(_,_)=>RefreshAdvancedStatus();dictationAdvanced.Collapsed+=(_,_)=>RefreshAdvancedStatus();
        var card=new StackPanel();card.Children.Add(dictationAdvanced);card.Children.Add(Ui.SettingRow(dictationAdvancedSummary));
        return Ui.Card(card);
    }

    StackPanel CompatibilitySection()
    {
        var delivery=new StackPanel();delivery.Children.Add(Ui.Text("Application compatibility",18,true));
        delivery.Children.Add(Ui.Text("Outlook inserts after Finish. Apps with a failed live insertion use final-only delivery on the next recording.",14));
        delivery.Children.Add(compatibility);
        resetCompatibility=Ui.Button("Re-enable live insertion for remembered apps",()=>
        {
            try
            {
                var saved=controller.ResetDeliveryCompatibility();Refresh();
                if(!saved)message.Text=compatibilityWarning=controller.ConnectionMessage;
                else
                {
                    if(message.Text==compatibilityWarning)message.Text="";
                    compatibilityWarning="";
                }
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){message.Text=compatibilityWarning="Could not save compatibility settings. "+ex.Message;}
            RefreshAdvancedStatus();
        });
        resetCompatibility.HorizontalAlignment=HorizontalAlignment.Left;delivery.Children.Add(resetCompatibility);return delivery;
    }

    void RefreshAdvancedStatus()
    {
        var draft=controller.DictionaryEditorDraft;
        var warning=draft!=null||compatibilityWarning.Length>0||controller.PersonalDictionaryStatus.Contains("could not",StringComparison.OrdinalIgnoreCase);
        dictationAdvancedSummary.Text=draft!=null?"Unsaved dictionary changes in Advanced. "+(draft.Error??"Open Advanced to review or save them."):
            compatibilityWarning.Length>0?compatibilityWarning:controller.PersonalDictionaryStatus.Contains("could not",StringComparison.OrdinalIgnoreCase)?controller.PersonalDictionaryStatus:
            "Personal dictionary and application compatibility.";
        dictationAdvancedSummary.SetResourceReference(TextBlock.ForegroundProperty,warning?"Warning":"Muted");
        dictationAdvancedSummary.Visibility=warning||!dictationAdvanced.IsExpanded?Visibility.Visible:Visibility.Collapsed;
    }

    void MainSectionSelected(int index)
    {
        historyPanel?.SetActive(index==(int)MainSection.History);
        if(index==(int)MainSection.General)RefreshStartup();
        if(index==(int)MainSection.Connections)
        {
            RefreshPalaBridgeStatus();
            RefreshPalaActivity();
            if (!kuboCatalogRequested && controller.Settings.PalaIntegrationEnabled) _ = LoadKuboCatalog();
            palaBridgePoll.Start();
            palaActivityPoll.Start();
        }
        else
        {
            palaBridgePoll.Stop();
            palaActivityPoll.Stop();
        }
    }
    void RefreshStartup()
    {
        ApplyStartupState(controller.StartupState);
    }
    void ApplyStartupState(WindowsStartupState state)
    {
        refreshingStartup=true;
        try
        {
            startWithWindows.IsChecked=state.Enabled;startWithWindows.IsEnabled=state.CanChange;
            startupStatus.Text=state.Message;startupStatus.Visibility=state.Message.Length>0?Visibility.Visible:Visibility.Collapsed;
        }
        finally{refreshingStartup=false;}
        startupError.Visibility=startupError.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
    void ChangeStartup()
    {
        if(refreshingStartup||loading||updating)return;
        var result=controller.SetStartWithWindows(startWithWindows.IsChecked==true);
        startupError.Text=result.Success?"":result.Error;
        ApplyStartupState(result.State);
    }
}
