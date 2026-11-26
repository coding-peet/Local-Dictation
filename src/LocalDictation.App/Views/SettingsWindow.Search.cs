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
        void Add(string id,MainSection page,FrameworkElement target,string label,string description,string keywords="")
        {
            searchTargets.Add(id,((int)page,target));
            entries.Add(new(id,page.ToString(),label,description,keywords));
        }
        Add("microphone",MainSection.Dictation,microphone,"Microphone","Choose the recording input device.","audio input mic");
        Add("dictation-model",MainSection.Dictation,primary,"Dictation model","Choose an installed local speech recognition model.","primary whisper transcription");
        Add("language",MainSection.Dictation,language,"Language","Choose the spoken language or Auto detection.","Slovak English sk en");
        Add("preferred-languages",MainSection.Dictation,preferredLanguages,"Preferred Auto languages","Limit automatic language detection to supported preferred languages.","tags add remove sk en");
        Add("live-transcription",MainSection.Dictation,live,"Live transcription","Recognize speech while you record.","banner preview real time");
        Add("after-recording",MainSection.Dictation,afterRecording,"After recording","Insert directly or Preview first in Review. Verification works with either choice.","finish output automatic paste manual");
        Add("live-insertion",MainSection.Dictation,insert,"Insert live text at the cursor","Update the original validated field while recording. Available with Insert directly and Live transcription.","delivery typing paste cursor");
        Add("private-recording",MainSection.Dictation,privateNext,"Private for next microphone recording","Keep the next microphone recording out of saved History and Review.","privacy private incognito memory");
        Add("dictionary-hints",MainSection.Dictation,dictionaryHints,"Recognition hints","Prepare personal names and technical terms; activation waits for paired evaluation.","dictionary vocabulary spelling names");
        Add("dictionary-corrections",MainSection.Dictation,dictionaryCorrections,"Exact corrections","Prepare exact case-sensitive replacement pairs; activation waits for paired evaluation.","dictionary replace terminology");
        Add("verification",MainSection.Dictation,verify,"Verify after recording","Compare with a second local model. Preview first always opens Review.","verification compare accuracy differences");
        Add("verification-model",MainSection.Dictation,verifier,"Verification model","Choose another eligible installed model when Verify after recording is on.","second compare whisper");
        Add("installed-models",MainSection.Models,installedModels,"Installed models","Review model tests and live support.","whisper local download");
        Add("test-model",MainSection.Models,testModel,"Test selected model","Test a local recording with the selected installed model.","audio file runtime");
        Add("remove-model",MainSection.Models,removeModel,"Remove selected model","Remove a safely verified app-owned model and keep saved dictations.","delete disk");
        Add("available-models",MainSection.Models,catalogModels,"Available models","Choose a model to add from the catalog.","install download");
        Add("add-model",MainSection.Models,repository,"Public Hugging Face URL","Check size and compatibility before downloading a public model.","add install repository custom");
        Add("windows-startup",MainSection.General,startWithWindows,"Start with Windows","Start in the system tray when you sign in, without recording.","startup logon login boot background autorun");
        Add("theme",MainSection.General,theme,"Color theme","Follow Windows or choose Light or Dark.","appearance system");
        Add("enable-shortcut",MainSection.General,shortcutEnabled,"Enable keyboard shortcut","Enable the global recording shortcut.","hotkey keyboard");
        Add("shortcut",MainSection.General,shortcutValue,"Start / stop recording","Press a key combination to set the recording shortcut. Default Ctrl+Shift.","hotkey key binding");
        Add("shortcut-mode",MainSection.General,shortcutHold,"Recording shortcut behavior","Press to start/stop or Hold to record until you release the keys.","hold press release toggle push to talk mode");
        Add("history-retention",MainSection.General,historyRetention,"Keep recordings for","Choose how long audio is kept. Transcripts stay until you delete them.","history retention days week month privacy storage audio lifetime expire delete");
        Add("service-address",MainSection.Connections,endpoint,"Local service address","The loopback session service supplies models and transcriptions.","endpoint API port localhost connection reconnect");
        Add("pala-sync",MainSection.Connections,palaIntegration,"Enable Note sync","Sync the paired Note over USB, Bluetooth or Wi-Fi through Bridge.","pala kubo sync integration Wi-Fi Bluetooth USB");
        Add("pala-note-settings",MainSection.Connections,palaOpenSettings,"Open Note settings",
            "Manage the Note, Bridge preferences, weather and pairing through your local browser.",
            "pala kubo bridge forecast weather location city country temperature rename USB Bluetooth pairing Wi-Fi");
        Add("pala-bridge-model",MainSection.Connections,palaBridgeModel,"Note transcription model","Use the Dictation model or choose a separate installed model.","pala kubo bridge slovak asr");
        Add("kubo-model",MainSection.Connections,palaKuboModel,"Processing model","Select a model from the signed-in Codex account.","pala codex sorting model");
        Add("kubo-effort",MainSection.Connections,palaKuboEffort,"Processing reasoning effort","Choose a reasoning level for the selected Codex model.","pala codex high xhigh effort thinking");
        Add("pala-transcripts",MainSection.Connections,palaTranscriptButton,"Note transcripts","Read original transcripts and inspect Bridge decisions.","pala calendar missing saved history log");
        Add("compatibility",MainSection.Dictation,compatibility,"Application compatibility","Outlook inserts after Finish. Remembered applications use final-only delivery with Insert directly.","Outlook paused live insertion reset");
        shell.Search.SetEntries(entries);
        shell.Search.Selected+=OpenSearchSetting;
    }

    void OpenSearchSetting(SettingsSearchEntry entry)
    {
        if(!searchTargets.TryGetValue(entry.Id,out var result))return;
        navigation.SelectedIndex=result.Page;
        shell.Search.Clear();
        if(entry.Id=="service-address")serviceAdvanced.IsExpanded=true;
        if(entry.Id is "dictionary-hints" or "dictionary-corrections" or "compatibility")dictationAdvanced.IsExpanded=true;
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
