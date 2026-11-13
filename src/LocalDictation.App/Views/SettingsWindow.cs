using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using DataGridTextColumn=System.Windows.Controls.DataGridTextColumn;
using System.Windows.Input;
using System.Windows.Threading;
using LocalDictation.Core;
using LocalDictation.App.Services;

namespace LocalDictation.App.Views;

public sealed class SettingsWindow : Window
{
    readonly AppController controller;
    readonly ListBox navigation=new(){VerticalAlignment=VerticalAlignment.Stretch};
    HistoryPanel? historyPanel;
    public void SelectHistory()=>navigation.SelectedIndex=2;
    readonly TextBlock connection=Ui.Text(""), shortcut=Ui.Text(""), message=Ui.Text(""), importStatus=Ui.Text(""), modelStatus=Ui.Text(""), verificationStatus=Ui.Text(""), saveStatus=Ui.Text("");
    readonly TextBox endpoint=new(), repository=new();
    readonly TextBox shortcutValue=new();
    readonly ComboBox theme=new(){ItemsSource=new[]{"System","Light","Dark"}};
    readonly ComboBox microphone=new(){DisplayMemberPath="Name",SelectedValuePath="Id"},
        primary=new(){DisplayMemberPath="Label",SelectedValuePath="Id"},
        verifier=new(){DisplayMemberPath="Label",SelectedValuePath="Id"},
        language=new();
    readonly ToggleButton live=Ui.Switch("Live transcription"),
        verify=Ui.Switch("Verify after recording"),insert=Ui.Switch("Insert live text at the cursor"),
        shortcutEnabled=Ui.Switch("Enable keyboard shortcut");
    readonly ListBox catalogModels=new(){MinHeight=96,MaxHeight=160,DisplayMemberPath="Label",SelectedValuePath="Id"};
    readonly StackPanel catalogChoice=new();
    readonly DataGrid installedModels=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,
        SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,HeadersVisibility=DataGridHeadersVisibility.Column,
        MinHeight=110,MaxHeight=230,RowHeight=48,Margin=new(0,8,0,8),HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly TextBlock installedEmpty=Ui.Text("",13),removalReason=Ui.Text("",12),modelActionStatus=Ui.Text("",13);
    readonly StackPanel verificationDetails=new(){Margin=new(0,8,0,0)};
    readonly TextBlock compatibility=Ui.Text("",12);
    Button resetCompatibility=null!;
    Button download=null!,cancelDownload=null!,checkModel=null!,testModel=null!,removeModel=null!;
    readonly Button record,recovery;
    readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=6,Margin=new(0,6,0,8)};
    readonly DispatcherTimer poll=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly DispatcherTimer saveDelay=new(){Interval=TimeSpan.FromMilliseconds(650)};
    readonly HashSet<TextBox> pendingText=[];
    readonly Dictionary<TextBox,string> textErrors=[];
    string selectionError="",saveError="",microphoneWarning="",verificationWarning="";
    string? inspection,job,missingMicrophoneId;
    string? downloadEndpoint;
    static readonly Dictionary<string,string> activeDownloads=new(StringComparer.OrdinalIgnoreCase);
    bool modelActionBusy,inspecting,closed;
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
        Title="Local Dictation";Width=1040;Height=820;MinWidth=640;MinHeight=620;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        record=Ui.Button("Record in banner",RecordInBanner,true);
        recovery=Ui.Button("Resolve clipboard recovery",controller.ShowClipboardRecoveryDetails);
        recovery.Visibility=Visibility.Collapsed;
        var pages=new[]{DictationPage(),ModelsPage(),HistoryPage(),PreferencesPage()};
        var shell=new MainWindowShell(navigation,pages,record,Ui.Button("Help",OpenHelp),
            connection,saveStatus,message,recovery,index=>historyPanel?.SetActive(index==2));
        Content=shell;
        controller.Changed+=Refresh;
        Closing+=(_,_)=>CommitTextEdits();
        Closed+=(_,_)=>{closed=true;controller.Changed-=Refresh;shell.Dispose();poll.Stop();saveDelay.Stop();historyPanel?.Dispose();};
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
        ResumeDownload();
    }

    static void ApplyNavigationContrast(ListBox navigation,bool highContrast)=>
        MainWindowShell.ApplyNavigationContrast(navigation,highContrast);

    void OpenHelp()
    {
        if(!LocalHelp.TryOpen(out var error))message.Text=error;
    }

    readonly WrapPanel preferredLanguages=new();
    string preferredLanguageSignature="";
    void RefreshLanguageChoices()
    {
        var selected=primary.SelectedItem as ModelInfo;
        var code=language.SelectedItem as string??controller.Settings.Language;
        var supported=new[]{"auto"}.Concat(selected?.Languages.Where(l=>l!="auto")??controller.Settings.PreferredLanguages).Distinct().ToArray();
        if(!(language.ItemsSource as IEnumerable<string>??[]).SequenceEqual(supported))language.ItemsSource=supported;
        language.SelectedItem=supported.Contains(code)?code:code=="auto"?"auto":supported.FirstOrDefault(l=>l!="auto");
        var signature=string.Join(",",controller.Settings.PreferredLanguages)+"/"+string.Join(",",LanguagePolicy.Available(controller.Models).Select(l=>l.Code));
        if(signature==preferredLanguageSignature)return;preferredLanguageSignature=signature;
        preferredLanguages.Children.Clear();
        preferredLanguages.Children.Add(new PreferredLanguagesPanel(controller.Settings.PreferredLanguages,controller.Models,
            code=>
            {
                var settings=Snapshot();settings.PreferredLanguages.Remove(code);Persist(settings);
            },
            code=>{var settings=Snapshot();settings.PreferredLanguages.Add(code);Persist(settings);}));
    }

    UIElement DictationPage()
    {
        var page=new StackPanel{Margin=new(0,8,8,0)};
        var recording=new StackPanel();
        var microphoneRow=new DockPanel();
        var refresh=Ui.Button("Refresh microphones",LoadMicrophones);refresh.Margin=new(16,8,0,8);refresh.MinHeight=48;
        DockPanel.SetDock(refresh,Dock.Right);microphoneRow.Children.Add(refresh);microphoneRow.Children.Add(microphone);
        Ui.Label(recording,"Microphone",microphoneRow);
        System.Windows.Automation.AutomationProperties.SetName(microphone,"Microphone");
        var modelRow=new Grid();modelRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});modelRow.ColumnDefinitions.Add(new(){Width=new GridLength(160)});
        var dictationChoice=new StackPanel{Margin=new(0,0,16,0)};Ui.Label(dictationChoice,"Dictation model",primary);modelRow.Children.Add(dictationChoice);
        var languageChoice=new StackPanel();Ui.Label(languageChoice,"Language",language);Grid.SetColumn(languageChoice,1);modelRow.Children.Add(languageChoice);recording.Children.Add(modelRow);
        Ui.Label(recording,"Preferred Auto languages (supported by installed models)",preferredLanguages);
        modelStatus.FontSize=12;modelStatus.Margin=new(0,0,0,10);modelStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");recording.Children.Add(modelStatus);
        recording.Children.Add(Ui.SwitchRow("Live transcription",live,"Recognize speech while you record."));recording.Children.Add(Ui.SwitchRow("Insert live text at the cursor",insert,"Updates only the original, validated field."));page.Children.Add(Ui.Card(recording));
        page.Children.Add(Ui.SwitchRow("Verify after recording",verify,"Compare with a second local model. Matching results insert automatically."));
        Ui.Label(verificationDetails,"Verification model",verifier);
        verificationStatus.FontSize=12;verificationStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");verificationDetails.Children.Add(verificationStatus);
        page.Children.Add(verificationDetails);
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxWidth=880,HorizontalAlignment=HorizontalAlignment.Stretch};
    }

    UIElement PreferencesPage()
    {
        var page=new StackPanel{Margin=new(0,8,8,0)};
        var appearance=new StackPanel();appearance.Children.Add(Ui.Text("Appearance",18,true));
        Ui.Label(appearance,"Color theme",theme,"System follows Windows. Light and Dark override it.");
        page.Children.Add(Section(appearance));
        var keyboard=new StackPanel();keyboard.Children.Add(Ui.Text("Keyboard shortcut",18,true));keyboard.Children.Add(Ui.SwitchRow("Enable keyboard shortcut",shortcutEnabled));
        Ui.Label(keyboard,"Start / stop recording",shortcutValue,"Examples: Ctrl+Shift, Ctrl+Alt+Space, Ctrl+Shift+D, Alt+F9.");
        shortcut.FontSize=12;keyboard.Children.Add(shortcut);
        var recheck=Ui.Button("Recheck shortcut",()=>{controller.RecheckShortcut();Refresh();});recheck.HorizontalAlignment=HorizontalAlignment.Left;keyboard.Children.Add(recheck);page.Children.Add(Section(keyboard));
        var service=new StackPanel();service.Children.Add(Ui.Text("Local transcription service",18,true));
        Ui.Label(service,"Local service address",endpoint,"The session service supplies your installed models and transcriptions.");
        service.Children.Add(Ui.Row(Ui.Button("Reconnect",()=>_ = Reconnect()),Ui.Button("Start local API",()=>_ = controller.StartLocalApi())));
        page.Children.Add(Section(service));
        var delivery=new StackPanel();delivery.Children.Add(Ui.Text("Application compatibility",20));
        delivery.Children.Add(Ui.Text("Outlook inserts after Finish. Apps with a failed live insertion use final-only delivery on the next recording.",14));
        delivery.Children.Add(compatibility);
        resetCompatibility=Ui.Button("Re-enable live insertion for remembered apps",()=>
        {
            try{var saved=controller.ResetDeliveryCompatibility();Refresh();message.Text=saved?"Remembered applications cleared. Outlook still inserts after Finish.":controller.ConnectionMessage;}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){message.Text="Could not save compatibility settings. "+ex.Message;}
        });
        resetCompatibility.HorizontalAlignment=HorizontalAlignment.Left;delivery.Children.Add(resetCompatibility);page.Children.Add(Section(delivery));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxWidth=880,HorizontalAlignment=HorizontalAlignment.Stretch};
    }

    UIElement ModelsPage()
    {
        var page=new StackPanel{Margin=new(0,8,8,0)};
        var installed=new StackPanel();installed.Children.Add(Ui.Text("Installed models",18,true));
        installedModels.Columns.Add(new DataGridTextColumn{Header="Model",Binding=new Binding(nameof(InstalledModelRow.Name)),Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=140});
        installedModels.Columns.Add(new DataGridTextColumn{Header="Test",Binding=new Binding(nameof(InstalledModelRow.Test)),Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=110});
        installedModels.Columns.Add(new DataGridTextColumn{Header="Live",Binding=new Binding(nameof(InstalledModelRow.Live)),Width=new DataGridLength(120),MinWidth=100});
        System.Windows.Automation.AutomationProperties.SetName(installedModels,"Installed models");
        installedModels.SelectionChanged+=(_,_)=>UpdateModelActions();
        installed.Children.Add(installedModels);installed.Children.Add(installedEmpty);
        testModel=Ui.Button("Test selected model with audio…",()=>_ = TestModel());
        removeModel=Ui.Button("Remove selected model",()=>_ = RemoveModel());
        var installedActions=new WrapPanel();installedActions.Children.Add(testModel);installedActions.Children.Add(removeModel);installed.Children.Add(installedActions);
        removalReason.SetResourceReference(TextBlock.ForegroundProperty,"Muted");installed.Children.Add(removalReason);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(modelActionStatus,System.Windows.Automation.AutomationLiveSetting.Polite);
        installed.Children.Add(modelActionStatus);page.Children.Add(Ui.Card(installed));

        var add=new StackPanel();add.Children.Add(Ui.Text("Add model",18,true));
        Ui.Label(catalogChoice,"Available models",catalogModels);add.Children.Add(catalogChoice);
        catalogModels.SelectionChanged+=(_,_)=>
        {
            if(loading)return;
            inspection=null;download.IsEnabled=false;
            if(catalogModels.SelectedItem is ModelInfo model)repository.Text="https://huggingface.co/"+model.Id;
        };
        Ui.Label(add,"Public Hugging Face URL",repository);
        checkModel=Ui.Button("Check size and compatibility",()=>_ = CheckModel());
        var start=Ui.Button("Download model",()=>_ = DownloadModel(),true);
        start.IsEnabled=false;download=start;
        var cancel=Ui.Button("Cancel download",()=>_ = CancelDownload());cancel.IsEnabled=false;cancelDownload=cancel;
        var addActions=new WrapPanel();addActions.Children.Add(checkModel);addActions.Children.Add(start);addActions.Children.Add(cancel);
        add.Children.Add(addActions);add.Children.Add(progress);add.Children.Add(importStatus);page.Children.Add(Ui.Card(add));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxWidth=880,HorizontalAlignment=HorizontalAlignment.Stretch};
    }

    sealed record InstalledModelRow(ModelInfo Model)
    {
        public string Id=>Model.Id;
        public string Name=>string.IsNullOrWhiteSpace(Model.Name)?Model.Id:Model.Name;
        public string Test=>Model.RuntimeTested?"Tested":"Not tested";
        public string Live=>Model.LiveReady?"Ready":Model.LiveSupported?"Available":"After recording";
    }

    ModelInfo? SelectedInstalledModel()=>(installedModels.SelectedItem as InstalledModelRow)?.Model;

    void UpdateModelActions()
    {
        var model=SelectedInstalledModel();
        testModel.IsEnabled=model!=null&&controller.Connected&&!controller.IsBusy&&!modelActionBusy;
        removeModel.IsEnabled=model?.CanRemove==true&&controller.Connected&&!controller.IsBusy&&!modelActionBusy&&job==null;
        removalReason.Text=model==null?"Select an installed model.":
            !model.CanRemove?string.IsNullOrWhiteSpace(model.RemovalRestriction)?"This model cannot be removed here.":model.RemovalRestriction:
            job!=null?"Wait for the download to finish before removing a model.":
            controller.IsBusy?"Wait for the current recording or transcription to finish.":
            !controller.Connected?"Reconnect to manage this model.":"";
        removalReason.Visibility=removalReason.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }

    UIElement HistoryPage()=>historyPanel=new HistoryPanel(controller){Margin=new(0,8,8,0)};

    static FrameworkElement Section(FrameworkElement content)
    {return Ui.Card(content);}

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
            resetCompatibility.Visibility=controller.Settings.FinalOnlyApplications.Count>0?Visibility.Visible:Visibility.Collapsed;
            compatibility.Text=controller.Settings.FinalOnlyApplications.Count==0?"No applications remembered.":"Final-only: "+string.Join(", ",controller.Settings.FinalOnlyApplications.OrderBy(name=>name,StringComparer.OrdinalIgnoreCase));
            shortcut.Text=controller.ShortcutMessage;
            record.Content=controller.IsRecording?"Stop recording":"Record in banner";
            System.Windows.Automation.AutomationProperties.SetName(record,(string)record.Content);
            record.IsEnabled=!controller.HasClipboardRecovery&&(controller.IsRecording||controller.Connected);
            recovery.Visibility=controller.HasClipboardRecovery?Visibility.Visible:Visibility.Collapsed;
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
            var selectedInstalled=SelectedInstalledModel()?.Id;
            var installed=controller.Models.Where(m=>m.Installed).Select(m=>new InstalledModelRow(m)).ToList();
            installedModels.ItemsSource=installed;
            installedModels.SelectedItem=installed.FirstOrDefault(m=>m.Id==selectedInstalled)??installed.FirstOrDefault(m=>m.Id==controller.Settings.PrimaryModel)??installed.FirstOrDefault();
            installedEmpty.Text=controller.Connected?"No models installed.":"Reconnect to view installed models.";
            installedEmpty.Visibility=installed.Count==0?Visibility.Visible:Visibility.Collapsed;
            UpdateModelActions();
            var selectedCatalog=catalogModels.SelectedValue as string;
            var available=controller.Models.Where(m=>!m.Installed&&m.CanInstall).ToList();
            catalogModels.ItemsSource=available;
            catalogModels.SelectedValue=available.FirstOrDefault(m=>m.Id==selectedCatalog)?.Id??available.FirstOrDefault()?.Id;
            catalogChoice.Visibility=available.Count>0?Visibility.Visible:Visibility.Collapsed;
            repository.Text=ModelRepositorySelection.AfterRefresh(repository.Text,selectedCatalog,(catalogModels.SelectedItem as ModelInfo)?.Id);
            checkModel.IsEnabled=!inspecting&&job==null;

        }
        finally{loading=false;updating=false;}
    }

    void UpdateModelChoices(bool userChange)
    {
        var selected=primary.SelectedItem as ModelInfo;
        RefreshLanguageChoices();
        var previous=userChange||selectionPending?verifier.SelectedValue as string??controller.Settings.VerificationModel:controller.Settings.VerificationModel;
        var choices=controller.Models.Where(m=>selected!=null&&m.Id!=selected.Id&&ModelEligibility.CanVerify(m,language.SelectedItem as string??"sk",selected,controller.Settings.PreferredLanguages)).ToList();
        verifier.ItemsSource=choices;
        var eligible=choices.FirstOrDefault(m=>m.Id==previous);
        if(userChange)eligible??=choices.FirstOrDefault();
        verifier.SelectedValue=eligible?.Id;
        var verificationWasEnabled=verify.IsChecked==true;
        if(userChange&&selected!=null&&choices.Count==0&&verificationWasEnabled)verify.IsChecked=false;
        verifier.IsEnabled=verify.IsChecked==true&&choices.Count>0;
        verificationDetails.Visibility=verify.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
        var unavailableSavedVerifier=!userChange&&verify.IsChecked==true&&eligible==null&&controller.Models.Count>0;
        verificationStatus.SetResourceReference(TextBlock.ForegroundProperty,unavailableSavedVerifier?"Warning":"Muted");
        verificationStatus.Text=controller.Models.Count==0?"Service offline; your saved verification choice is retained.":
            unavailableSavedVerifier?"The saved verification model is unavailable or incompatible. Choose another model or turn verification off.":
            choices.Count==0?"Verification is off: no other installed, runtime-tested model supports this language.":
            eligible!=null&&eligible.Id!=previous&&verify.IsChecked==true?$"Verification will use {eligible.Name}; it must differ from Dictation.":"";
        if(userChange&&verificationWasEnabled&&verify.IsChecked!=true)
        {
            // Keep a rejected toggle explicit even though its optional controls are collapsed.
            verificationWarning=verificationStatus.Text;message.Text=verificationWarning;
        }
        else if(choices.Count>0&&verificationWarning.Length>0)
        {
            if(message.Text==verificationWarning)message.Text="";
            verificationWarning="";
        }
        live.IsEnabled=selected?.LiveSupported==true;
        if(userChange&&selected!=null&&!live.IsEnabled)live.IsChecked=false;
        modelStatus.Text=selected==null?controller.Models.Count==0
            ?$"Service offline; saved model retained: {controller.Settings.PrimaryModel}."
            :"The saved model is unavailable. Select an installed model.":
            !selected.Installed?"This catalog model is not installed.":
            !selected.RuntimeTested?"Installed. Run a model test before first use.":
            !selected.LiveSupported?"After-recording transcription only.":
            !selected.LiveReady?"Live preview latency may be several seconds.":"";
        modelStatus.Visibility=modelStatus.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
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
            if(verify.IsChecked==true&&controller.Models.Count>0&&(verification==null||model==null||!controller.Models.Any(m=>m.Id==verification&&m.Id!=model.Id&&ModelEligibility.CanVerify(m,selectedLanguage,model,value.PreferredLanguages))))
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

    async Task TestModel()
    {
        if(SelectedInstalledModel() is not ModelInfo model){modelActionStatus.Text="Select an installed model.";return;}
        if(modelActionBusy||controller.IsBusy)return;
        var picker=new Microsoft.Win32.OpenFileDialog{Title="Test a local recording",Filter="Audio files|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.opus;*.webm|All files|*.*"};
        if(picker.ShowDialog(this)!=true)return;
        modelActionBusy=true;UpdateModelActions();
        modelActionStatus.Text=$"Testing {model.Name}…";try
        {
            using var api=new ApiClient(controller.Settings);
            using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var requested=language.SelectedItem as string??"auto";
            var testLanguage=model.Languages.Contains(requested)?requested:model.Languages.Contains("auto")?"auto":model.Languages.FirstOrDefault()??"auto";
            await api.TestModel(model,picker.FileName,testLanguage,cancellation.Token);
            modelActionStatus.Text=$"{model.Name}: test passed.";
            await controller.RefreshConnection();Refresh();
        }
        catch(Exception ex){modelActionStatus.Text=ex is InvalidOperationException?ex.Message:"The model test did not finish. Try again.";}
        finally{modelActionBusy=false;UpdateModelActions();}
    }

    async Task RemoveModel()
    {
        if(SelectedInstalledModel() is not ModelInfo model||!model.CanRemove||modelActionBusy||controller.IsBusy||job!=null)return;
        if(MessageBox.Show(this,$"Remove {model.Name}?\n\nIts downloaded model files will be deleted. Saved dictations will be kept.","Remove model",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        modelActionBusy=true;UpdateModelActions();modelActionStatus.Text=$"Removing {model.Name}…";
        try
        {
            using var api=new ApiClient(controller.Settings);
            var result=await api.RemoveModel(model.Id);
            modelActionStatus.Text=string.IsNullOrWhiteSpace(result.Message)?result.Removed?"Model removed.":"The model could not be removed.":result.Message;
            if(result.CleanupPending)modelActionStatus.Text+=" Some model files still need cleanup.";
            await controller.RefreshConnection();Refresh();
        }
        catch(Exception ex){modelActionStatus.Text=ex is InvalidOperationException?ex.Message:"The model could not be removed. Reconnect and try again.";}
        finally{modelActionBusy=false;UpdateModelActions();}
    }

    async Task CheckModel()
    {
        var selected=catalogModels.SelectedItem as ModelInfo;
        var value=repository.Text.Trim().TrimEnd('/');
        var catalogId=selected!=null&&(value.Length==0||value=="https://huggingface.co/"+selected.Id)?selected.Id:null;
        await InspectModel(catalogId);
    }

    async Task InspectModel(string? catalogId=null)
    {
        if(inspecting||job!=null)return;
        var value=repository.Text.Trim();
        if(value.Length==0&&catalogId==null){importStatus.Text="Enter a public Hugging Face model URL.";return;}
        inspection=null;inspecting=true;checkModel.IsEnabled=false;
        importStatus.Text="Checking model…";download.IsEnabled=false;
        try
        {
            using var api=new ApiClient(controller.Settings);
            var result=await api.Post("/v1/model-imports/inspect",new{repository=value,catalog_model_id=catalogId});
            if(repository.Text.Trim()!=value)return;
            inspection=result.GetProperty("inspection_id").GetString();
            var bytes=result.GetProperty("download_bytes").GetInt64();
            importStatus.Text=$"Compatible · {bytes/1073741824.0:0.00} GiB download";
            download.IsEnabled=true;
        }
        catch(Exception ex){importStatus.Text=ex is InvalidOperationException?ex.Message:"The model could not be checked. Check the URL and connection.";}
        finally{inspecting=false;checkModel.IsEnabled=job==null;}
    }

    async Task DownloadModel()
    {
        if(inspection==null||job!=null)return;
        download.IsEnabled=false;checkModel.IsEnabled=false;
        var settings=Snapshot();downloadEndpoint=settings.Endpoint;
        try
        {
            using var api=new ApiClient(settings);
            var result=await api.Post("/v1/model-imports",new{inspection_id=inspection});
            job=result.GetProperty("id").GetString();inspection=null;
            if(job!=null)activeDownloads[downloadEndpoint]=job;
            cancelDownload.IsEnabled=true;UpdateModelActions();
            if(!closed){poll.Start();await PollDownload();}
        }
        catch(Exception ex){importStatus.Text=ex is InvalidOperationException?ex.Message:"Download could not start.";checkModel.IsEnabled=true;}
    }

    void ResumeDownload()
    {
        if(!activeDownloads.TryGetValue(controller.Settings.Endpoint,out var active))return;
        downloadEndpoint=controller.Settings.Endpoint;job=active;checkModel.IsEnabled=false;cancelDownload.IsEnabled=true;
        UpdateModelActions();poll.Start();_ = PollDownload();
    }

    async Task PollDownload()
    {
        if(job==null||polling)return;polling=true;
        try
        {
            var settings=Snapshot();settings.Endpoint=downloadEndpoint??settings.Endpoint;
            using var api=new ApiClient(settings);
            var result=await api.Get("/v1/model-imports/"+job);
            var done=result.GetProperty("downloaded_bytes").GetInt64();
            var total=result.GetProperty("total_bytes").GetInt64();
            var state=result.GetProperty("state").GetString();
            progress.Value=total>0?100.0*done/total:0;
            importStatus.Text=$"{state} · {done/1048576.0:0.0} / {total/1048576.0:0.0} MiB";
            if(state is "completed" or "cancelled" or "failed")
            {
                poll.Stop();cancelDownload.IsEnabled=false;
                if(downloadEndpoint!=null&&activeDownloads.GetValueOrDefault(downloadEndpoint)==job)activeDownloads.Remove(downloadEndpoint);
                job=null;checkModel.IsEnabled=true;UpdateModelActions();
                if(result.TryGetProperty("message",out var detail))importStatus.Text+="\n"+detail.GetString();
                if(state=="completed"){importStatus.Text+="\nModel installed.";await controller.RefreshConnection();Refresh();}
            }
        }
        catch(Exception){importStatus.Text="Download status unavailable. Reconnect to the API to check it.";}
        finally{polling=false;}
    }

    async Task CancelDownload()
    {
        if(job==null)return;
        var settings=Snapshot();settings.Endpoint=downloadEndpoint??settings.Endpoint;
        using var api=new ApiClient(settings);
        try{await api.Post("/v1/model-imports/"+job+"/cancel",new{});await PollDownload();}
        catch(Exception){importStatus.Text="Could not request cancellation. Check the API before starting another download.";}
    }
}
