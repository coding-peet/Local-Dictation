using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalDictation.Core;
using LocalDictation.App.Services;

namespace LocalDictation.App.Views;

public sealed class SettingsWindow : Window
{
    readonly AppController controller;
    readonly TextBlock connection=Ui.Text(""), shortcut=Ui.Text(""), message=Ui.Text(""), importStatus=Ui.Text(""), modelStatus=Ui.Text("");
    readonly TextBox endpoint=new(), repository=new();
    readonly ComboBox microphone=new(){DisplayMemberPath="Name",SelectedValuePath="Id"},
        primary=new(){DisplayMemberPath="Label",SelectedValuePath="Id"},
        verifier=new(){DisplayMemberPath="Label",SelectedValuePath="Id"},
        language=new(){ItemsSource=new[]{"sk","en","auto"}};
    readonly CheckBox live=new(){Content="Live transcription (experimental)"},
        verify=new(){Content="Verify after recording"},insert=new(){Content="Insert directly at the cursor"},
        shortcutEnabled=new(){Content="Enable Ctrl+Shift shortcut"};
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
        Title="Local Dictation";Width=790;Height=820;MinWidth=610;MinHeight=540;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel{Margin=new(24)};
        var header=new StackPanel();header.Children.Add(Ui.Text("Local Dictation",24,true));
        header.Children.Add(Ui.Text("Local speech, wherever you write.",14));
        connection.Foreground=Ui.Brush("Muted");header.Children.Add(connection);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);

        var footer=new StackPanel{Margin=new(0,12,0,0)};
        var actions=new WrapPanel();
        foreach(var button in new[]{
            Ui.Button("_Save settings",()=>Save(),true),
            Ui.Button("_Reconnect",()=>_ = Reconnect()),
            record=Ui.Button("Record to Review",()=>{if(Save())controller.Toggle();}),
            Ui.Button("Close",Close)})
        {button.Margin=new(0,0,8,8);actions.Children.Add(button);}
        footer.Children.Add(actions);message.Foreground=Ui.Brush("Warning");footer.Children.Add(message);
        footer.Children.Add(Ui.Text("Audio stays in RAM · 10-minute limit · Only pending Review is saved",12));
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var tabs=new TabControl();
        tabs.Items.Add(new TabItem{Header="_Dictation",Content=DictationPage()});
        tabs.Items.Add(new TabItem{Header="_Models",Content=ModelsPage()});
        tabs.Items.Add(new TabItem{Header="_Pending Review",Content=ReviewPage()});
        root.Children.Add(tabs);Content=root;
        controller.Changed+=Refresh;
        Closed+=(_,_)=>{controller.Changed-=Refresh;poll.Stop();};
        poll.Tick+=(_,_)=>_ = PollDownload();
        language.SelectionChanged+=(_,_)=>{if(!loading)UpdateModelChoices();};
        primary.SelectionChanged+=(_,_)=>{if(!loading)UpdateModelChoices();};
        repository.TextChanged+=(_,_)=>{inspection=null;download.IsEnabled=false;};
        Refresh(true);
    }

    UIElement DictationPage()
    {
        var page=new StackPanel{Margin=new(16)};
        Ui.Label(page,"API address",endpoint,"Loopback session API. The existing batch-only server remains separate during development.");
        Ui.Label(page,"Microphone",microphone,"Choose the actual microphone, not an unused headset input.");
        page.Children.Add(Ui.Button("Refresh microphones",LoadMicrophones));
        Ui.Label(page,"Dictation model",primary);
        page.Children.Add(live);page.Children.Add(modelStatus);
        Ui.Label(page,"Language",language);
        page.Children.Add(insert);
        page.Children.Add(Ui.Text("When the cursor or window changes, delivery stops and the complete text opens in native Review.",12));
        page.Children.Add(verify);
        Ui.Label(page,"Verification model",verifier,"A second pass checks the original recording after Stop. Review lets you choose each differing phrase.");
        page.Children.Add(shortcutEnabled);page.Children.Add(shortcut);
        page.Children.Add(Ui.Button("Recheck shortcut",()=>{controller.RecheckShortcut();Refresh();}));
        page.Children.Add(Ui.Text("Use Ctrl+Shift in the destination field. Record to Review is for microphone checks from this window.",12));
        return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    UIElement ModelsPage()
    {
        var page=new StackPanel{Margin=new(16)};
        page.Children.Add(Ui.Text("Original models",18,true));
        page.Children.Add(Ui.Text("The Dictation dropdown lists installed models and the initial checked catalog. Runtime-tested and quality-checked are separate states.",13));
        page.Children.Add(Ui.Text("Test the selected Dictation model with a local audio file before using it for Verification. Testing sends audio only to this loopback API.",13));
        page.Children.Add(Ui.Button("Test selected model with audio…",()=>_ = TestModel()));
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
        page.Children.Add(Ui.Text("Unfinished text and choices are encrypted for this Windows user. Successfully inserted dictations are not archived. Audio is temporary and unavailable after inference.",13));
        page.Children.Add(reviews);
        reviews.MouseDoubleClick+=(_,_)=>OpenSelected();
        page.Children.Add(Ui.Button("_Open selected Review",OpenSelected));
        page.Children.Add(Ui.Text("Copy and Close keep an item. Use Dismiss inside Review to remove it.",12));
        return page;
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
            record.Content=controller.IsRecording?"Stop recording":"Record to Review";
            record.IsEnabled=controller.Connected;
            var selected=primary.SelectedValue as string??controller.Settings.PrimaryModel;
            primary.ItemsSource=controller.Models;primary.SelectedValue=selected;
            if(initial)
            {
                endpoint.Text=controller.Settings.Endpoint;
                live.IsChecked=controller.Settings.Live;verify.IsChecked=controller.Settings.VerifyAfterRecording;
                insert.IsChecked=controller.Settings.InsertAtCursor;shortcutEnabled.IsChecked=controller.Settings.EnableShortcut;
                language.SelectedItem=controller.Settings.Language;LoadMicrophones();
            }
            UpdateModelChoices();
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
            selected.LiveSupported&&!selected.LiveReady?"Live transcription is experimental. Accuracy and latency have not passed acceptance for everyday use.":
            selected.LiveSupported?"Live runtime is ready.":"After-recording model. Live capability has not been verified.";
    }

    bool Save()
    {
        try
        {
            if(primary.SelectedItem is not ModelInfo model||!model.Installed)
                throw new ArgumentException("Select an installed Dictation model.");
            if(microphone.SelectedIndex<0)throw new ArgumentException("Select an available microphone.");
            var selectedLanguage=language.SelectedItem as string??"sk";
            if(selectedLanguage!="auto"&&!model.Languages.Contains(selectedLanguage))
                throw new ArgumentException("The selected model does not support this language.");
            var verification=verifier.SelectedValue as string;
            if(verify.IsChecked==true&&(verification==null||!controller.Models.Any(m=>m.Id==verification&&ModelEligibility.CanVerify(m,selectedLanguage,model))))throw new ArgumentException("Choose an eligible Verification model.");
            controller.SaveSettings(new Settings{
                Endpoint=endpoint.Text.Trim(),MicrophoneId=microphone.SelectedValue as string,
                PrimaryModel=model.Id,VerificationModel=verification,
                VerifyAfterRecording=verify.IsChecked==true,InsertAtCursor=insert.IsChecked==true,
                Live=live.IsChecked==true,Language=language.SelectedItem as string??"sk",
                EnableShortcut=shortcutEnabled.IsChecked==true});
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
            var value=controller.Settings;
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

    async Task InspectModel()
    {
        importStatus.Text="Inspecting public repository metadata…";download.IsEnabled=false;
        try
        {
            using var api=new ApiClient(controller.Settings);
            var result=await api.Post("/v1/model-imports/inspect",new{repository=repository.Text.Trim()});
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
