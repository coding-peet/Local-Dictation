using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

public sealed partial class SettingsWindow
{
    readonly Dictionary<string,(int Page,FrameworkElement Target)> searchTargets=[];

    void OnSettingsKey(object sender,KeyEventArgs e)
    {
        if(shortcutCapturing)return;
        if(e.Key==Key.F&&Keyboard.Modifiers==ModifierKeys.Control)
        {shell.Search.FocusSearch();e.Handled=true;return;}
        if(e.Key==Key.Escape&&shell.Search.IsQueryActive)
        {shell.Search.Clear();shell.Search.FocusSearch();e.Handled=true;return;}
        if(e.Key!=Key.F1||Keyboard.Modifiers!=ModifierKeys.None)return;
        e.Handled=true;if(!e.IsRepeat)OpenHelp();
    }

    void ConfigureSettingsSearch()
    {
        // Read-only compatibility status can receive search focus when no reset action exists.
        compatibility.Focusable=true;KeyboardNavigation.SetIsTabStop(compatibility,false);
        var entries=new List<SettingsSearchEntry>();
        void Add(string id,int page,FrameworkElement target,string label,string description,string keywords="")
        {
            searchTargets.Add(id,(page,target));
            entries.Add(new(id,page switch{0=>"Dictation",1=>"Models",_=>"Settings"},label,description,keywords));
        }
        Add("microphone",0,microphone,"Microphone","Choose the recording input device.","audio input mic");
        Add("dictation-model",0,primary,"Dictation model","Choose an installed local speech recognition model.","primary whisper transcription");
        Add("language",0,language,"Language","Choose the spoken language or Auto detection.","Slovak English sk en");
        Add("preferred-languages",0,preferredLanguages,"Preferred Auto languages","Limit automatic language detection to supported preferred languages.","tags add remove sk en");
        Add("live-transcription",0,live,"Live transcription","Recognize speech while you record.","banner preview real time");
        Add("after-recording",0,afterRecording,"After recording","Insert directly or Preview first in Review. Verification works with either choice.","finish output automatic paste manual");
        Add("live-insertion",0,insert,"Insert live text at the cursor","Update the original validated field while recording. Available with Insert directly and Live transcription.","delivery typing paste cursor");
        Add("verification",0,verify,"Verify after recording","Compare with a second local model. Preview first always opens Review.","verification compare accuracy differences");
        Add("verification-model",0,verifier,"Verification model","Choose another eligible installed model when Verify after recording is on.","second compare whisper");
        Add("installed-models",1,installedModels,"Installed models","Review model tests and live support.","whisper local download");
        Add("test-model",1,testModel,"Test selected model","Test a local recording with the selected installed model.","audio file runtime");
        Add("remove-model",1,removeModel,"Remove selected model","Remove a safely verified app-owned model and keep saved dictations.","delete disk");
        Add("available-models",1,catalogModels,"Available models","Choose a model to add from the catalog.","install download");
        Add("add-model",1,repository,"Public Hugging Face URL","Check size and compatibility before downloading a public model.","add install repository custom");
        Add("theme",3,theme,"Color theme","Follow Windows or choose Light or Dark.","appearance system");
        Add("enable-shortcut",3,shortcutEnabled,"Enable keyboard shortcut","Enable the global recording shortcut.","hotkey keyboard");
        Add("shortcut",3,shortcutValue,"Start / stop recording","Press a key combination to set the recording shortcut. Default Ctrl+Shift.","hotkey key binding");
        Add("shortcut-mode",3,shortcutHold,"Recording shortcut behavior","Press to start/stop or Hold to record until you release the keys.","hold press release toggle push to talk mode");
        Add("history-retention",3,historyRetention,"Keep recordings for","Choose how long audio is kept. Transcripts stay until you delete them.","history retention days week month privacy storage audio lifetime expire delete");
        Add("service-address",3,endpoint,"Local service address","The loopback session service supplies models and transcriptions.","endpoint API port localhost connection reconnect");
        Add("compatibility",3,compatibility,"Application compatibility","Outlook inserts after Finish. Remembered applications use final-only delivery with Insert directly.","Outlook paused live insertion reset");
        shell.Search.SetEntries(entries);
        shell.Search.Selected+=OpenSearchSetting;
    }

    void OpenSearchSetting(SettingsSearchEntry entry)
    {
        if(!searchTargets.TryGetValue(entry.Id,out var result))return;
        navigation.SelectedIndex=result.Page;
        shell.Search.Clear();
        var target=SearchTarget(entry.Id,result.Target);
        // Search navigation changes only the selected page, scrolling and focus.
        // It never enables a prerequisite or applies a setting.
        void Reveal()
        {
            target.BringIntoView();
            if(target.Focusable&&target.IsEnabled)target.Focus();
            else target.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        Reveal();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,(Action)Reveal);
    }

    FrameworkElement SearchTarget(string id,FrameworkElement target)=>id switch
    {
        "verification-model" when verificationDetails.Visibility!=Visibility.Visible=>verify,
        "live-insertion" when afterRecording.SelectedIndex==1=>afterRecording,
        "live-transcription" or "live-insertion" when !live.IsEnabled=>primary,
        "live-insertion" when live.IsChecked!=true=>live,
        "available-models" when catalogChoice.Visibility!=Visibility.Visible=>repository,
        "compatibility" when resetCompatibility.Visibility==Visibility.Visible=>resetCompatibility,
        _=>target
    };

    void UpdateOutputDependencies()
    {
        var preview=afterRecording.SelectedIndex==1;
        liveInsertRow.IsEnabled=!preview&&live.IsChecked==true;
        outputDependency.Text=preview
            ?"Preview first keeps your destination unchanged. Live insertion is paused; your choice is remembered."
            :live.IsChecked!=true?"Live insertion is available when Live transcription is on. Your choice is remembered.":"";
        outputDependency.Visibility=outputDependency.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
}
