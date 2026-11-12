using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LocalDictation.Core;
using LocalDictation.App.Services;

namespace LocalDictation.App.Views;

public sealed class SettingsWindow : Window
{
    readonly AppController controller;
    readonly TextBlock connection=Ui.Text(""), shortcut=Ui.Text(""), message=Ui.Text(""), importStatus=Ui.Text(""), modelStatus=Ui.Text(""), verificationStatus=Ui.Text(""), saveStatus=Ui.Text("");
    readonly TextBox endpoint=new(), repository=new();
    readonly TextBox shortcutValue=new();
    readonly ComboBox theme=new(){ItemsSource=new[]{"System","Light","Dark"}};
    readonly ComboBox microphone=new(){DisplayMemberPath="Name",SelectedValuePath="Id"},
        primary=new(){DisplayMemberPath="Label",SelectedValuePath="Id"},
        verifier=new(){DisplayMemberPath="Label",SelectedValuePath="Id"},
        language=new(){ItemsSource=new[]{"sk","en","auto"}};
    readonly CheckBox live=new(){Content="Live transcription"},
        verify=new(){Content="Verify after recording"},insert=new(){Content="Insert live text at the cursor"},
        shortcutEnabled=new(){Content="Enable keyboard shortcut"};
    readonly ListBox catalogModels=new(){MinHeight=96,MaxHeight=160,DisplayMemberPath="Label",SelectedValuePath="Id"};
    Button inspectCatalog=null!;
    readonly ListBox reviews=new(){MinHeight=70,MaxHeight=170,DisplayMemberPath="Reason"};
    Button download=null!,cancelDownload=null!;
    readonly Button record;
    readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=6,Margin=new(0,6,0,8)};
    readonly DispatcherTimer poll=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly DispatcherTimer saveDelay=new(){Interval=TimeSpan.FromMilliseconds(650)};
    readonly HashSet<TextBox> pendingText=[];
    readonly Dictionary<TextBox,string> textErrors=[];
    string selectionError="",saveError="",microphoneWarning="";
    string? inspection,job,missingMicrophoneId;
    bool loading,updating,polling,selectionPending,microphoneEnumerationFailed;
    public SettingsWindow(AppController controller)
    {
        this.controller=controller;
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key!=Key.F1||Keyboard.Modifiers!=ModifierKeys.None)return;
            e.Handled=true;
            if(!e.IsRepeat)OpenHelp();
        };
        Title="Local Dictation";Width=920;Height=800;MinWidth=740;MinHeight=620;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel{Margin=new(20)};
        var header=new StackPanel();
        var titleRow=new DockPanel();
        record=Ui.Button("Record in banner",RecordInBanner,true);
        record.HorizontalAlignment=HorizontalAlignment.Right;record.Margin=new(16,0,0,0);
        DockPanel.SetDock(record,Dock.Right);titleRow.Children.Add(record);
        titleRow.Children.Add(Ui.Text("Local Dictation",22,true));header.Children.Add(titleRow);
        connection.SetResourceReference(TextBlock.ForegroundProperty,"Muted");connection.FontSize=13;connection.Margin=new(0,3,0,6);header.Children.Add(connection);
        saveStatus.FontSize=13;saveStatus.Margin=new(0,0,0,16);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(saveStatus,System.Windows.Automation.AutomationLiveSetting.Polite);
        header.Children.Add(saveStatus);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);

        var footer=new DockPanel{Margin=new(0,14,0,0)};
        var actions=Ui.Row(Ui.Button("Help (F1)",OpenHelp),Ui.Button("Close",Close));
        actions.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);
        var notes=new StackPanel{Margin=new(0,0,12,0)};
        message.SetResourceReference(TextBlock.ForegroundProperty,"Warning");notes.Children.Add(message);
        notes.Children.Add(Ui.Text("Latest 20 dictations saved locally · 10-minute recording limit",12));
        footer.Children.Add(notes);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var tabs=new TabControl();
        tabs.Items.Add(new TabItem{Header="_Dictation",Content=DictationPage()});
        tabs.Items.Add(new TabItem{Header="_Models",Content=ModelsPage()});
        tabs.Items.Add(new TabItem{Header="_Pending Review",Content=ReviewPage()});
        tabs.Items.Add(new TabItem{Header="_History",Content=HistoryPage()});
        tabs.Items.Add(new TabItem{Header="_Settings",Content=PreferencesPage()});
        root.Children.Add(tabs);Content=root;
        controller.Changed+=Refresh;
        Closing+=(_,_)=>CommitTextEdits();
        Closed+=(_,_)=>{controller.Changed-=Refresh;poll.Stop();saveDelay.Stop();};
        poll.Tick+=(_,_)=>_ = PollDownload();
        foreach(var box in new[]{microphone,primary,verifier,language,theme})box.SelectionChanged+=(_,_)=>SelectionChanged(box);
        foreach(var check in new[]{live,verify,insert,shortcutEnabled})
        {check.Checked+=(_,_)=>SelectionChanged(check);check.Unchecked+=(_,_)=>SelectionChanged(check);}
        foreach(var text in new[]{endpoint,shortcutValue})
        {
            text.TextChanged+=(_,_)=>QueueTextEdit(text);
            text.LostKeyboardFocus+=(_,_)=>CommitTextEdits(text);
        }
        saveDelay.Tick+=(_,_)=>CommitTextEdits();
        repository.TextChanged+=(_,_)=>{inspection=null;download.IsEnabled=false;};
        Refresh(true);UpdateSaveStatus();
    }

    void OpenHelp()
    {
        if(!LocalHelp.TryOpen(out var error))message.Text=error;
    }

    UIElement DictationPage()
    {
        var page=new StackPanel{Margin=new(0,18,4,0)};
        var recording=new StackPanel();recording.Children.Add(Ui.Text("Recording",18,true));
        recording.Children.Add(Ui.Text("Choose what you record and which model transcribes it.",13));
        var microphoneRow=new DockPanel();
        var refresh=Ui.Button("Refresh microphones",LoadMicrophones);refresh.Margin=new(10,0,0,0);refresh.MinHeight=40;
        DockPanel.SetDock(refresh,Dock.Right);microphoneRow.Children.Add(refresh);microphoneRow.Children.Add(microphone);
        Ui.Label(recording,"Microphone",microphoneRow);
        System.Windows.Automation.AutomationProperties.SetName(microphone,"Microphone");
        var modelRow=new Grid();modelRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});modelRow.ColumnDefinitions.Add(new(){Width=new GridLength(160)});
        var dictationChoice=new StackPanel{Margin=new(0,0,16,0)};Ui.Label(dictationChoice,"Dictation model",primary);modelRow.Children.Add(dictationChoice);
        var languageChoice=new StackPanel();Ui.Label(languageChoice,"Language",language);Grid.SetColumn(languageChoice,1);modelRow.Children.Add(languageChoice);recording.Children.Add(modelRow);
        recording.Children.Add(live);modelStatus.FontSize=12;modelStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");recording.Children.Add(modelStatus);
        page.Children.Add(Section(recording));
        var delivery=new StackPanel();delivery.Children.Add(Ui.Text("Text and verification",18,true));
        delivery.Children.Add(insert);delivery.Children.Add(verify);
        delivery.Children.Add(Ui.Text("Start with the shortcut in your destination field. Live insertion can be turned off while the banner shows your draft. Finished text returns automatically; if verification finds differences, choose their wording in the banner first.",12));
        Ui.Label(delivery,"Verification model",verifier,"An optional second pass. Review lets you choose between the two versions.");
        verificationStatus.FontSize=12;verificationStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");delivery.Children.Add(verificationStatus);
        page.Children.Add(Section(delivery));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    UIElement PreferencesPage()
    {
        var page=new StackPanel{Margin=new(0,18,4,0)};
        var appearance=new StackPanel();appearance.Children.Add(Ui.Text("Appearance",18,true));
        Ui.Label(appearance,"Color theme",theme,"System follows Windows. Light and Dark override it.");
        page.Children.Add(Section(appearance));
        var keyboard=new StackPanel();keyboard.Children.Add(Ui.Text("Keyboard shortcut",18,true));keyboard.Children.Add(shortcutEnabled);
        Ui.Label(keyboard,"Start / stop recording",shortcutValue,"Examples: Ctrl+Shift, Ctrl+Alt+Space, Ctrl+Shift+D, Alt+F9.");
        shortcut.FontSize=12;keyboard.Children.Add(shortcut);
        var recheck=Ui.Button("Recheck shortcut",()=>{controller.RecheckShortcut();Refresh();});recheck.HorizontalAlignment=HorizontalAlignment.Left;keyboard.Children.Add(recheck);page.Children.Add(Section(keyboard));
        var service=new StackPanel();service.Children.Add(Ui.Text("Local transcription service",18,true));
        Ui.Label(service,"Local service address",endpoint,"The session service supplies your installed models and transcriptions.");
        service.Children.Add(Ui.Row(Ui.Button("Reconnect",()=>_ = Reconnect()),Ui.Button("Start local API",()=>_ = controller.StartLocalApi())));
        page.Children.Add(Section(service));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    UIElement ModelsPage()
    {
        var page=new StackPanel{Margin=new(0,18,4,0)};
        page.Children.Add(Ui.Text("Original models",18,true));
        page.Children.Add(Ui.Text("The Dictation dropdown lists installed models and the initial checked catalog. Runtime-tested and quality-checked are separate states.",13));
        page.Children.Add(Ui.Text("Test the selected Dictation model with a local audio file before using it for Verification. Testing sends audio only to this loopback API.",13));
        page.Children.Add(Ui.Button("Test selected model with audio…",()=>_ = TestModel()));
        page.Children.Add(Ui.Text("Install or repair a catalog model",18,true));
        page.Children.Add(Ui.Text("Select a missing model, inspect its pinned download and size, then use Download. Installed models are preserved.",13));
        page.Children.Add(catalogModels);
        inspectCatalog=Ui.Button("Inspect selected catalog model",()=>_ = InspectCatalog());
        page.Children.Add(inspectCatalog);
        catalogModels.SelectionChanged+=(_,_)=>inspectCatalog.IsEnabled=catalogModels.SelectedItem is ModelInfo m&&m.CanInstall;
        page.Children.Add(Ui.Text("Add a public Hugging Face model",18,true));
        Ui.Label(page,"Repository URL or owner/name",repository,"Built-in Whisper Transformers models with original safetensors only. Private, gated and remote-code models are rejected.");
        page.Children.Add(Ui.Button("_Inspect compatibility and size",()=>_ = InspectModel()));
        var start=Ui.Button("_Download inspected model",()=>_ = DownloadModel(),true);
        start.IsEnabled=false;download=start;
        var cancel=Ui.Button("Cancel download",()=>_ = CancelDownload());cancel.IsEnabled=false;cancelDownload=cancel;
        page.Children.Add(Ui.Row(start,cancel));page.Children.Add(progress);page.Children.Add(importStatus);
        page.Children.Add(Ui.Text("Downloading does not prove transcription quality or live performance. A new model starts in after-recording mode. Closing this window does not cancel its download.",12));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    UIElement ReviewPage()
    {
        var page=new StackPanel{Margin=new(0,18,4,0)};
        page.Children.Add(Ui.Text("Pending Review",18,true));
        page.Children.Add(Ui.Text("Unfinished text and choices are protected for this Windows user. The latest 20 dictations and their audio are also available in History.",13));
        page.Children.Add(reviews);
        reviews.MouseDoubleClick+=(_,_)=>OpenSelected();
        page.Children.Add(Ui.Button("_Open selected Review",OpenSelected));
        page.Children.Add(Ui.Text("Copy and Close keep an item. Use Dismiss inside Review to remove it.",12));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    UIElement HistoryPage()
    {
        var page=new StackPanel{Margin=new(0,18,4,0)};
        page.Children.Add(Ui.Text("Recent dictations",18,true));
        page.Children.Add(Ui.Text("Keep the latest 20 transcripts and recordings. Open History to play audio, copy text, export a WAV, or remove an item.",13));
        var open=Ui.Button("Open transcript and audio history",controller.ShowHistory);
        open.HorizontalAlignment=HorizontalAlignment.Left;page.Children.Add(open);return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    static FrameworkElement Section(FrameworkElement content)
    {content.Margin=new(0,0,0,24);return content;}

    void LoadMicrophones()
    {
        var selected=microphone.SelectedValue as string??controller.Settings.MicrophoneId??"";
        List<MicrophoneInfo> devices;var enumerated=true;
        try{devices=MicrophoneCapture.Devices();}
        catch(Exception){devices=[];enumerated=false;}
        ApplyMicrophoneDevices(devices,selected,enumerated);
    }

    void ApplyMicrophoneDevices(List<MicrophoneInfo> devices,string selected,bool enumerated)
    {
        var wasLoading=loading;loading=true;
        microphoneEnumerationFailed=!enumerated;
        try
        {
            devices.Insert(0,new("","System default microphone"));
            missingMicrophoneId=null;
            if(selected.Length>0&&!devices.Any(device=>device.Id==selected))
            {
                missingMicrophoneId=selected;devices.Add(new(selected,"Saved microphone (currently unavailable)"));
            }
            if(!enumerated)ShowMicrophoneWarning("No microphone could be enumerated. Check Windows microphone access.");
            else if(missingMicrophoneId!=null)ShowMicrophoneWarning("The saved microphone is unavailable. Choose an active device before recording.");
            else ClearMicrophoneWarning();
            microphone.ItemsSource=devices;microphone.SelectedValue=selected;
        }
        finally{loading=wasLoading;}
    }

    void ShowMicrophoneWarning(string warning){microphoneWarning=warning;message.Text=warning;}
    void ClearMicrophoneWarning()
    {
        if(message.Text==microphoneWarning)message.Text="";
        microphoneWarning="";
    }

    void Refresh() => Refresh(false);
    void Refresh(bool initial)
    {
        if(updating)return;updating=true;loading=true;
        try
        {
            connection.Text=controller.ConnectionMessage;
            shortcut.Text=controller.ShortcutMessage;
            record.Content=controller.IsRecording?"Stop recording":"Record in banner";
            System.Windows.Automation.AutomationProperties.SetName(record,(string)record.Content);
            record.IsEnabled=controller.IsRecording||controller.Connected;
            var selected=primary.SelectedValue as string??controller.Settings.PrimaryModel;
            primary.ItemsSource=controller.Models;primary.SelectedValue=selected;
            if(initial)
            {
                endpoint.Text=controller.Settings.Endpoint;
                live.IsChecked=controller.Settings.Live;verify.IsChecked=controller.Settings.VerifyAfterRecording;
                insert.IsChecked=controller.Settings.InsertAtCursor;shortcutEnabled.IsChecked=controller.Settings.EnableShortcut;
                shortcutValue.Text=controller.Settings.Shortcut;theme.SelectedItem=controller.Settings.Theme;
                language.SelectedItem=controller.Settings.Language;LoadMicrophones();
            }
            UpdateModelChoices(false);
            UpdateSaveStatus();
            var selectedCatalog=catalogModels.SelectedValue as string;
            catalogModels.ItemsSource=controller.Models;
            catalogModels.SelectedValue=selectedCatalog??controller.Models.FirstOrDefault(m=>m.CanInstall)?.Id??controller.Models.FirstOrDefault()?.Id;
            inspectCatalog.IsEnabled=catalogModels.SelectedItem is ModelInfo item&&item.CanInstall;
            var selectedReview=(reviews.SelectedItem as ReviewDocument)?.Id;
            reviews.ItemsSource=controller.Pending;
            reviews.SelectedItem=controller.Pending.FirstOrDefault(p=>p.Id==selectedReview)??controller.Pending.FirstOrDefault();
        }
        finally{loading=false;updating=false;}
    }

    void UpdateModelChoices(bool userChange)
    {
        var selected=primary.SelectedItem as ModelInfo;
        var previous=userChange||selectionPending?verifier.SelectedValue as string??controller.Settings.VerificationModel:controller.Settings.VerificationModel;
        var choices=controller.Models.Where(m=>selected!=null&&m.Id!=selected.Id&&ModelEligibility.CanVerify(m,language.SelectedItem as string??"sk",selected)).ToList();
        verifier.ItemsSource=choices;
        var eligible=choices.FirstOrDefault(m=>m.Id==previous);
        if(userChange)eligible??=choices.FirstOrDefault();
        verifier.SelectedValue=eligible?.Id;
        if(userChange&&selected!=null&&choices.Count==0&&verify.IsChecked==true)verify.IsChecked=false;
        verifier.IsEnabled=verify.IsChecked==true&&choices.Count>0;
        var unavailableSavedVerifier=!userChange&&verify.IsChecked==true&&eligible==null&&controller.Models.Count>0;
        verificationStatus.SetResourceReference(TextBlock.ForegroundProperty,unavailableSavedVerifier?"Warning":"Muted");
        verificationStatus.Text=controller.Models.Count==0?"Service offline; your saved verification choice is retained.":
            unavailableSavedVerifier?"The saved verification model is unavailable or incompatible. Choose another model or turn verification off.":
            choices.Count==0?"Verification is off: no other installed, runtime-tested model supports this language.":
            eligible!=null&&eligible.Id!=previous&&verify.IsChecked==true?$"Verification will use {eligible.Name}; it must differ from Dictation.":"";
        live.IsEnabled=selected?.LiveSupported==true;
        if(userChange&&selected!=null&&!live.IsEnabled)live.IsChecked=false;
        modelStatus.Text=selected==null?controller.Models.Count==0
            ?$"Service offline; saved model retained: {controller.Settings.PrimaryModel}."
            :"The saved model is unavailable. Select an installed model.":
            !selected.Installed?"This catalog model is not installed.":
            !selected.RuntimeTested?$"{selected.Name} · Installed. Run a model test before first use.":
            !selected.LiveSupported?$"{selected.Name} · After-recording transcription only.":
            !selected.LiveReady?$"{selected.Name} · Live transcription available. Preview latency can be several seconds; larger models may update more slowly.":
            $"{selected.Name} · Live preview or after-recording transcription.";
    }

    void SelectionChanged(Control source)
    {
        if(loading)return;
        selectionPending=true;
        if(source==microphone&&!microphoneEnumerationFailed&&microphone.SelectedIndex>=0&&microphone.SelectedValue as string!=missingMicrophoneId)ClearMicrophoneWarning();
        var modelChange=source==primary||source==verifier||source==language||source==live||source==verify;
        if(modelChange)
        {
            loading=true;
            try{UpdateModelChoices(true);}
            finally{loading=false;}
        }
        SaveSelections(modelChange);
    }

    Settings Snapshot()=>JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(controller.Settings))!;

    bool SaveSelections(bool requireValidModel=true)
    {
        selectionPending=true;
        var value=Snapshot();
        try
        {
            var model=primary.SelectedItem as ModelInfo;
            if(controller.Models.Count>0&&(model==null||!model.Installed))
                throw new ArgumentException("Select an installed Dictation model.");
            var selectedLanguage=language.SelectedItem as string??controller.Settings.Language;
            if(model!=null&&selectedLanguage!="auto"&&!model.Languages.Contains(selectedLanguage))
                throw new ArgumentException("The selected model does not support this language.");
            var verification=verifier.SelectedValue as string??(controller.Models.Count==0?controller.Settings.VerificationModel:null);
            if(verify.IsChecked==true&&controller.Models.Count>0&&(verification==null||model==null||!controller.Models.Any(m=>m.Id==verification&&m.Id!=model.Id&&ModelEligibility.CanVerify(m,selectedLanguage,model))))
                throw new ArgumentException("Choose an eligible Verification model.");
            value.PrimaryModel=model?.Id??controller.Settings.PrimaryModel;value.VerificationModel=verification;
            value.VerifyAfterRecording=verify.IsChecked==true;value.Live=live.IsChecked==true;value.Language=selectedLanguage;
            selectionError="";
        }
        catch(ArgumentException ex)
        {
            selectionError=ex.Message;
            if(requireValidModel){UpdateSaveStatus();return false;}
        }
        // An absent device or invalid model draft must not block unrelated valid settings.
        // Keep the saved microphone ID until the user explicitly selects another microphone.
        if(microphone.SelectedIndex>=0)value.MicrophoneId=microphone.SelectedValue as string;
        value.InsertAtCursor=insert.IsChecked==true;value.EnableShortcut=shortcutEnabled.IsChecked==true;
        value.Theme=theme.SelectedItem as string??"System";
        var saved=Persist(value);selectionPending=!saved||selectionError.Length>0;
        UpdateSaveStatus();return saved;
    }

    bool Persist(Settings value)
    {
        saveStatus.Text="Saving…";
        try
        {
            // SaveSettings writes before replacing the effective settings. Active runs own a separate snapshot.
            if(JsonSerializer.Serialize(value)!=JsonSerializer.Serialize(controller.Settings))controller.SaveSettings(value);
            saveError="";UpdateSaveStatus();return true;
        }
        catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException)
        {saveError=ex.Message;UpdateSaveStatus();return false;}
    }

    void QueueTextEdit(TextBox text)
    {
        if(loading)return;
        pendingText.Add(text);textErrors.Remove(text);
        saveDelay.Stop();saveDelay.Start();UpdateSaveStatus();
    }

    void CommitTextEdits(TextBox? only=null)
    {
        var edits=pendingText.Where(text=>only==null||text==only).ToArray();
        if(edits.Length==0)return;
        var value=Snapshot();var valid=new List<TextBox>();
        foreach(var text in edits)
        {
            try
            {
                if(text==endpoint)
                {var candidate=Snapshot();candidate.Endpoint=text.Text.Trim();candidate.ServerUri();value.Endpoint=candidate.Endpoint;}
                else value.Shortcut=ShortcutSpec.Parse(text.Text).ToString();
                textErrors.Remove(text);valid.Add(text);
            }
            catch(ArgumentException ex){textErrors[text]=ex.Message;pendingText.Remove(text);}
        }
        if(valid.Count>0&&Persist(value))
        {
            foreach(var text in valid)pendingText.Remove(text);
            // Retry an earlier failed discrete change; saving text must never hide an unapplied model.
            if(selectionPending)SaveSelections(false);
        }
        saveDelay.Stop();
        if(only!=null&&pendingText.Count>0&&saveError.Length==0)saveDelay.Start();
        UpdateSaveStatus();
    }

    void UpdateSaveStatus()
    {
        var discreteError=selectionPending&&selectionError.Length==0&&saveError.Length==0?"Your option changes are not saved. Select the choice again to retry.":"";
        var errors=new[]{selectionError,saveError,discreteError}.Where(error=>error.Length>0).Concat(textErrors.Values).Distinct().ToArray();
        var failed=errors.Length>0;
        saveStatus.SetResourceReference(TextBlock.ForegroundProperty,failed?"Warning":"Muted");
        saveStatus.Text=failed?"Not saved: "+string.Join(" ",errors):pendingText.Count>0?"Saving…":
            controller.IsBusy?"Saved automatically · Changes apply to the next recording.":"Saved automatically";
    }

    void RecordInBanner()
    {
        // Stopping must remain available even if a draft is invalid or the API disconnects.
        if(controller.IsRecording){controller.ToggleReview();return;}
        CommitTextEdits();
        if(!SaveSelections()||textErrors.Count>0||pendingText.Count>0||saveError.Length>0)return;
        if(microphone.SelectedIndex<0||microphone.SelectedValue as string==missingMicrophoneId)
        {ShowMicrophoneWarning("Choose an available microphone before recording. Your saved microphone choice is retained.");return;}
        controller.ToggleReview();
    }

    async Task Reconnect()
    {
        try
        {
            CommitTextEdits(endpoint);
            if(textErrors.ContainsKey(endpoint)||pendingText.Contains(endpoint))return;
            await controller.RefreshConnection();Refresh();
        }
        catch(Exception ex){message.Text=ex is ArgumentException?ex.Message:"Cannot connect to the session API.";}
    }
    void OpenSelected(){if(reviews.SelectedItem is ReviewDocument review)controller.OpenReview(review);}

    async Task TestModel()
    {
        if(primary.SelectedItem is not ModelInfo model||!model.Installed){message.Text="Select an installed Dictation model.";return;}
        var picker=new Microsoft.Win32.OpenFileDialog{Title="Test a local recording",Filter="Audio files|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.opus;*.webm|All files|*.*"};
        if(picker.ShowDialog(this)!=true)return;
        message.Text="Testing the original model…";try
        {
            using var api=new ApiClient(controller.Settings);
            using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await api.TestModel(model,picker.FileName,language.SelectedItem as string??"auto",cancellation.Token);
            message.Text="Runtime test passed. Transcription quality and live behavior still need a separate check.";
            await controller.RefreshConnection();Refresh();
        }
        catch(Exception ex){message.Text=ex is InvalidOperationException?ex.Message:"The runtime test did not finish. Your existing model selection was preserved.";}
    }

    async Task InspectCatalog()
    {
        if(catalogModels.SelectedItem is not ModelInfo model||!model.CanInstall)return;
        repository.Text="https://huggingface.co/"+model.Id+"/tree/"+model.Revision;
        await InspectModel(model.Id);
    }

    async Task InspectModel(string? catalogId=null)
    {
        importStatus.Text="Inspecting public repository metadata…";download.IsEnabled=false;
        try
        {
            using var api=new ApiClient(controller.Settings);
            var result=await api.Post("/v1/model-imports/inspect",new{repository=repository.Text.Trim(),catalog_model_id=catalogId});
            inspection=result.GetProperty("inspection_id").GetString();
            var bytes=result.GetProperty("download_bytes").GetInt64();
            importStatus.Text=$"Compatible original Whisper model · {bytes/1048576.0:0.0} MiB\nPinned commit: {result.GetProperty("revision").GetString()}\nNot runtime-tested or quality-checked.";
            download.IsEnabled=true;
        }
        catch(Exception ex){importStatus.Text=ex is InvalidOperationException?ex.Message:"Model inspection failed. Check the URL and network connection.";}
    }

    async Task DownloadModel()
    {
        if(inspection==null)return;
        download.IsEnabled=false;
        try
        {
            using var api=new ApiClient(controller.Settings);
            var result=await api.Post("/v1/model-imports",new{inspection_id=inspection});
            job=result.GetProperty("id").GetString();cancelDownload.IsEnabled=true;poll.Start();
            await PollDownload();
        }
        catch(Exception ex){importStatus.Text=ex is InvalidOperationException?ex.Message:"Download could not start.";}
    }

    async Task PollDownload()
    {
        if(job==null||polling)return;polling=true;
        try
        {
            using var api=new ApiClient(controller.Settings);
            var result=await api.Get("/v1/model-imports/"+job);
            var done=result.GetProperty("downloaded_bytes").GetInt64();
            var total=result.GetProperty("total_bytes").GetInt64();
            var state=result.GetProperty("state").GetString();
            progress.Value=total>0?100.0*done/total:0;
            importStatus.Text=$"{state} · {done/1048576.0:0.0} / {total/1048576.0:0.0} MiB";
            if(state is "completed" or "cancelled" or "failed")
            {
                poll.Stop();cancelDownload.IsEnabled=false;
                if(result.TryGetProperty("message",out var detail))importStatus.Text+="\n"+detail.GetString();
                if(state=="completed"){importStatus.Text+="\nInstalled. Select the model and run a transcription test.";await controller.RefreshConnection();Refresh();}
            }
        }
        catch(Exception){importStatus.Text="Download status unavailable. Reconnect to the API to check it.";}
        finally{polling=false;}
    }

    async Task CancelDownload()
    {
        if(job==null)return;
        using var api=new ApiClient(controller.Settings);
        try{await api.Post("/v1/model-imports/"+job+"/cancel",new{});await PollDownload();}
        catch(Exception){importStatus.Text="Could not request cancellation. Check the API before starting another download.";}
    }
}
