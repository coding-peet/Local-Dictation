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
    readonly TextBlock connection=Ui.Text(""), shortcut=Ui.Text(""), message=Ui.Text(""), importStatus=Ui.Text(""), modelStatus=Ui.Text("");
    readonly TextBox endpoint=new(), repository=new();
    readonly TextBox shortcutValue=new();
    readonly ComboBox theme=new(){ItemsSource=new[]{"System","Light","Dark"}};
    readonly ComboBox microphone=new(){DisplayMemberPath="Name",SelectedValuePath="Id"},
        primary=new(){DisplayMemberPath="Label",SelectedValuePath="Id"},
        verifier=new(){DisplayMemberPath="Label",SelectedValuePath="Id"},
        language=new(){ItemsSource=new[]{"sk","en","auto"}};
    readonly CheckBox live=new(){Content="Live transcription (experimental)"},
        verify=new(){Content="Verify after recording"},insert=new(){Content="Insert live text at the cursor"},
        shortcutEnabled=new(){Content="Enable keyboard shortcut"};
    readonly ListBox catalogModels=new(){MinHeight=96,MaxHeight=160,DisplayMemberPath="Label",SelectedValuePath="Id"};
    Button inspectCatalog=null!;
    readonly ListBox reviews=new(){MinHeight=70,MaxHeight=170,DisplayMemberPath="Reason"};
    Button download=null!,cancelDownload=null!;
    readonly Button record;
    readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=6,Margin=new(0,6,0,8)};
    readonly DispatcherTimer poll=new(){Interval=TimeSpan.FromSeconds(1)};
    string? inspection,job;
    bool loading,updating,polling;
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
        var root=new DockPanel{Margin=new(24)};
        var header=new StackPanel();header.Children.Add(Ui.Text("Local Dictation",24,true));
        header.Children.Add(Ui.Text("Local speech, wherever you write.",14));
        connection.SetResourceReference(TextBlock.ForegroundProperty,"Muted");connection.FontSize=12;connection.Margin=new(0,6,0,20);header.Children.Add(connection);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);

        var footer=new StackPanel{Margin=new(0,12,0,0)};
        var actions=new WrapPanel();
        foreach(var button in new[]{
            record=Ui.Button("Record in banner",()=>{if(Save())controller.ToggleReview();},true),
            Ui.Button("_Save settings",()=>Save()),
            Ui.Button("Help (F1)",OpenHelp),
            Ui.Button("Close",Close)})
        {button.Margin=new(0,0,8,8);actions.Children.Add(button);}
        footer.Children.Add(actions);message.SetResourceReference(TextBlock.ForegroundProperty,"Warning");footer.Children.Add(message);
        footer.Children.Add(Ui.Text("Latest 20 transcripts and recordings saved locally · 10-minute recording limit",12));
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var tabs=new TabControl();
        tabs.Items.Add(new TabItem{Header="_Dictation",Content=DictationPage()});
        tabs.Items.Add(new TabItem{Header="_Models",Content=ModelsPage()});
        tabs.Items.Add(new TabItem{Header="_Pending Review",Content=ReviewPage()});
        tabs.Items.Add(new TabItem{Header="_History",Content=HistoryPage()});
        tabs.Items.Add(new TabItem{Header="_Settings",Content=PreferencesPage()});
        root.Children.Add(tabs);Content=root;
        controller.Changed+=Refresh;
        Closed+=(_,_)=>{controller.Changed-=Refresh;poll.Stop();};
        poll.Tick+=(_,_)=>_ = PollDownload();
        language.SelectionChanged+=(_,_)=>{if(!loading)UpdateModelChoices();};
        primary.SelectionChanged+=(_,_)=>{if(!loading)UpdateModelChoices();};
        repository.TextChanged+=(_,_)=>{inspection=null;download.IsEnabled=false;};
        Refresh(true);
    }

    void OpenHelp()
    {
        if(!LocalHelp.TryOpen(out var error))message.Text=error;
    }

    UIElement DictationPage()
    {
        var page=new StackPanel{Margin=new(18)};
        var recording=new StackPanel();recording.Children.Add(Ui.Text("Recording",18,true));
        recording.Children.Add(Ui.Text("Choose what you record and which model transcribes it.",13));
        Ui.Label(recording,"Microphone",microphone);
        var refresh=Ui.Button("Refresh microphones",LoadMicrophones);refresh.HorizontalAlignment=HorizontalAlignment.Left;refresh.Margin=new(0,0,0,18);recording.Children.Add(refresh);
        Ui.Label(recording,"Dictation model",primary);
        Ui.Label(recording,"Language",language);
        recording.Children.Add(live);modelStatus.FontSize=12;modelStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");recording.Children.Add(modelStatus);
        page.Children.Add(Ui.Card(recording));
        var delivery=new StackPanel();delivery.Children.Add(Ui.Text("Text and verification",18,true));
        delivery.Children.Add(insert);
        delivery.Children.Add(Ui.Text("Start with the shortcut in your destination field. Live insertion can be turned off while the banner shows your draft. Finished text returns automatically; if verification finds differences, choose their wording in the banner first.",12));
        delivery.Children.Add(verify);
        Ui.Label(delivery,"Verification model",verifier,"An optional second pass. Review lets you choose between the two versions.");
        page.Children.Add(Ui.Card(delivery));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    UIElement PreferencesPage()
    {
        var page=new StackPanel{Margin=new(18)};
        var appearance=new StackPanel();appearance.Children.Add(Ui.Text("Appearance",18,true));
        Ui.Label(appearance,"Color theme",theme,"System follows Windows. Light and Dark override it.");
        theme.SelectionChanged+=(_,_)=>{if(!loading&&theme.SelectedItem is string value)ThemeManager.Apply(value);};
        page.Children.Add(Ui.Card(appearance));
        var keyboard=new StackPanel();keyboard.Children.Add(Ui.Text("Keyboard shortcut",18,true));keyboard.Children.Add(shortcutEnabled);
        Ui.Label(keyboard,"Start / stop recording",shortcutValue,"Examples: Ctrl+Shift, Ctrl+Alt+Space, Ctrl+Shift+D, Alt+F9.");
        shortcut.FontSize=12;keyboard.Children.Add(shortcut);
        var recheck=Ui.Button("Recheck shortcut",()=>{controller.RecheckShortcut();Refresh();});recheck.HorizontalAlignment=HorizontalAlignment.Left;keyboard.Children.Add(recheck);page.Children.Add(Ui.Card(keyboard));
        var service=new StackPanel();service.Children.Add(Ui.Text("Local transcription service",18,true));
        Ui.Label(service,"Local service address",endpoint,"The session service supplies your installed models and transcriptions.");
        service.Children.Add(Ui.Row(Ui.Button("Reconnect",()=>_ = Reconnect()),Ui.Button("Start local API",()=>_ = controller.StartLocalApi())));
        page.Children.Add(Ui.Card(service));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    UIElement ModelsPage()
    {
        var page=new StackPanel{Margin=new(16)};
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
        var page=new StackPanel{Margin=new(16)};
        page.Children.Add(Ui.Text("Pending Review",18,true));
        page.Children.Add(Ui.Text("Unfinished text and choices are protected for this Windows user. The latest 20 dictations and their audio are also available in History.",13));
        page.Children.Add(reviews);
        reviews.MouseDoubleClick+=(_,_)=>OpenSelected();
        page.Children.Add(Ui.Button("_Open selected Review",OpenSelected));
        page.Children.Add(Ui.Text("Copy and Close keep an item. Use Dismiss inside Review to remove it.",12));
        return page;
    }
    UIElement HistoryPage()
    {
        var page=new StackPanel{Margin=new(16)};
        page.Children.Add(Ui.Text("Recent dictations",18,true));
        page.Children.Add(Ui.Text("Keep the latest 20 transcripts and recordings. Open History to play audio, copy text, export a WAV, or remove an item.",13));
        var open=Ui.Button("Open transcript and audio history",controller.ShowHistory);
        open.HorizontalAlignment=HorizontalAlignment.Left;page.Children.Add(open);return page;
    }

    void LoadMicrophones()
    {
        try
        {
            var selected=microphone.SelectedValue as string??controller.Settings.MicrophoneId;
            var devices=MicrophoneCapture.Devices();
            devices.Insert(0,new("","System default microphone"));
            microphone.ItemsSource=devices;microphone.SelectedValue=selected??"";
            if(microphone.SelectedIndex<0)message.Text="The saved microphone is unavailable. Choose an active device.";
        }
        catch(Exception){message.Text="No microphone could be enumerated. Check Windows microphone access.";}
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
            record.IsEnabled=controller.Connected;
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
            UpdateModelChoices();
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

    void UpdateModelChoices()
    {
        var selected=primary.SelectedItem as ModelInfo;
        var previous=verifier.SelectedValue as string??controller.Settings.VerificationModel;
        verifier.ItemsSource=controller.Models.Where(m=>selected!=null&&m.Id!=selected.Id&&ModelEligibility.CanVerify(m,language.SelectedItem as string??"sk",selected)).ToList();
        if(previous!=null)verifier.SelectedValue=previous;
        else if(verifier.Items.Count>0)verifier.SelectedIndex=0;
        live.IsEnabled=selected?.LiveSupported==true;
        if(selected!=null&&!live.IsEnabled)live.IsChecked=false;
        modelStatus.Text=selected==null?"The saved model is unavailable. Select an installed model.":
            !selected.Installed?"This catalog model is not installed.":
            !selected.RuntimeTested?"Installed · Run a model test before first use.":
            selected.LiveSupported?"Installed · Live preview or after-recording transcription":"Installed · After recording. This profile is not qualified for live transcription.";
    }

    bool Save()
    {
        try
        {
            var model=primary.SelectedItem as ModelInfo;
            if(controller.Models.Count>0&&(model==null||!model.Installed))
                throw new ArgumentException("Select an installed Dictation model.");
            if(microphone.SelectedIndex<0)throw new ArgumentException("Select an available microphone.");
            var selectedLanguage=language.SelectedItem as string??"sk";
            if(model!=null&&selectedLanguage!="auto"&&!model.Languages.Contains(selectedLanguage))
                throw new ArgumentException("The selected model does not support this language.");
            var verification=verifier.SelectedValue as string??(controller.Models.Count==0?controller.Settings.VerificationModel:null);
            if(verify.IsChecked==true&&controller.Models.Count>0&&(verification==null||model==null||!controller.Models.Any(m=>m.Id==verification&&ModelEligibility.CanVerify(m,selectedLanguage,model))))throw new ArgumentException("Choose an eligible Verification model.");
            controller.SaveSettings(new Settings{
                Endpoint=endpoint.Text.Trim(),MicrophoneId=microphone.SelectedValue as string,
                PrimaryModel=model?.Id??controller.Settings.PrimaryModel,VerificationModel=verification,
                VerifyAfterRecording=verify.IsChecked==true,InsertAtCursor=insert.IsChecked==true,
                Live=live.IsChecked==true,Language=language.SelectedItem as string??"sk",
                EnableShortcut=shortcutEnabled.IsChecked==true,Shortcut=ShortcutSpec.Parse(shortcutValue.Text).ToString(),
                Theme=theme.SelectedItem as string??"System"});
            message.Text=controller.IsBusy?"Saved. Changes apply to the next recording.":"Settings saved.";
            return true;
        }
        catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException)
        {message.Text=ex.Message;return false;}
    }

    async Task Reconnect()
    {
        try
        {
            var value=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(controller.Settings))!;
            value.Endpoint=endpoint.Text.Trim();value.ServerUri();controller.SaveSettings(value);
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
