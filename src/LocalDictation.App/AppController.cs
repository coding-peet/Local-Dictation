using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LocalDictation.Core;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using Forms=System.Windows.Forms;

namespace LocalDictation.App;

public sealed class AppController : IDisposable
{
    sealed class Run(Settings settings)
    {
        public Settings Settings=settings;
        public ReviewDocument Review=new() {PrimaryModel=settings.PrimaryModel,
            VerificationModel=settings.VerifyAfterRecording?settings.VerificationModel:null,
            Reason="Interrupted recording. Some draft text may already be in your destination."};
        public CancellationTokenSource Cancel=new();
        public MicrophoneCapture Capture=new();
        public SessionStream Stream=new();
        public WindowsTextDelivery? Delivery;
        public DestinationStamp Destination;
        public bool CanDeliver, NeedsReview, PrimaryFinal, Completed, Verifying, Recording=true;
        public DateTimeOffset Started=DateTimeOffset.Now;
        public string Text="";
    }

    readonly ReviewStore store=new(Path.Combine(AppPaths.Data,"review"));
    readonly PendingReviews pending;
    readonly Dictionary<string,ReviewWindow> reviewWindows=[];
    readonly AutomationWorker automation=new();
    readonly GlobalInput input;
    readonly Forms.NotifyIcon tray;
    readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(80)};
    readonly BannerWindow banner;
    readonly bool noHotkey;
    SettingsWindow? settingsWindow;
    Run? active;
    nint bannerAnchor;
    public nint BannerAnchorWindow=>bannerAnchor;
    DateTimeOffset hideAt=DateTimeOffset.MinValue;
    public Settings Settings {get;private set;}
    public List<ModelInfo> Models {get;private set;}=[];
    public IReadOnlyList<ReviewDocument> Pending => pending.Documents.Where(p=>p.Id!=active?.Review.Id||active?.Completed==true).ToList();
    public bool Connected {get;private set;}
    public string ConnectionMessage {get;private set;}="Checking the local API…";
    public string ShortcutMessage {get;private set;}="";
    public string StateLabel {get;private set;}="Ready";
    public string DetailLabel {get;private set;}="";
    public string LatestText => active?.Text??"";
    public float Level => active?.Capture.Level??0;
    public bool IsRecording => active?.Recording==true;
    public bool IsBusy => active!=null;
    public bool ShowBanner => IsBusy||DateTimeOffset.Now<hideAt;
    public event Action? Changed;

    public AppController(bool noHotkey)
    {
        this.noHotkey=noHotkey;
        Settings=Settings.Load();
        Settings.ServerUri();
        pending=new PendingReviews(store);
        if(store.Errors.Count>0)ConnectionMessage=$"{store.Errors.Count} protected Review item(s) could not be read. Files were preserved.";
        input=new GlobalInput();
        input.UserEdited+=()=>active?.Delivery?.Invalidate();
        input.Cancel+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(Cancel);
        input.Toggle+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(Toggle);
        RecheckShortcut();
        banner=new BannerWindow(this);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Start / stop dictation",null,(_,_)=>Toggle());
        menu.Items.Add("Settings",null,(_,_)=>ShowSettings());
        menu.Items.Add("Pending Review",null,(_,_)=>OpenLatestReview());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit",null,(_,_)=>Exit());
        tray=new Forms.NotifyIcon {Text="Local Dictation",Icon=System.Drawing.SystemIcons.Application,
            ContextMenuStrip=menu,Visible=true};
        tray.DoubleClick+=(_,_)=>ShowSettings();
        timer.Tick+=(_,_) =>
        {
            if(active!=null)
            {
                var elapsed=TimeSpan.FromSeconds(active.Capture.CapturedBytes/32000.0);
                DetailLabel=$"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00} · {Models.FirstOrDefault(m=>m.Id==active.Settings.PrimaryModel)?.Name??active.Settings.PrimaryModel}"+
                    (active.CanDeliver?" · At cursor":" · Review")+
                    " · "+active.Capture.DeviceName+(active.Recording?" · Mic active":" · Mic stopped");
            }
            banner.Refresh();
        };
        timer.Start();
    }

    public void RecheckShortcut()
    {
        var collision=GlobalInput.TypeWhisperRunning();
        input.ShortcutEnabled=Settings.EnableShortcut&&!noHotkey&&!collision;
        ShortcutMessage=noHotkey?"Shortcut disabled for this test launch (--no-hotkey).":
            collision?"TypeWhisper is running. Quit it, then Recheck shortcut before using Ctrl+Shift here.":
            !Settings.EnableShortcut?"Ctrl+Shift is disabled in Settings.":"Ctrl+Shift starts and stops dictation.";
        Changed?.Invoke();
    }

    public void SaveSettings(Settings settings)
    {
        settings.Save();
        Settings=settings;
        RecheckShortcut();
    }

    public async Task RefreshConnection()
    {
        try
        {
            using var api=new ApiClient(Settings);
            var health=await api.Get("/health");
            if(!health.TryGetProperty("protocol_version",out var protocol)||protocol.GetInt32()!=1)
                throw new InvalidOperationException("This server supports batch transcription only. Start the Local Dictation session API.");
            Models=await api.Models();
            Connected=health.GetProperty("ready").GetBoolean();
            ConnectionMessage=Connected?(health.GetProperty("busy").GetBoolean()?"API connected · GPU busy":"API connected · Ready"):
                "API preparing or unavailable. Wait for runtime readiness, then Reconnect.";
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or ArgumentException)
        {
            Connected=false;
            ConnectionMessage=ex is InvalidOperationException?ex.Message:"Local API unavailable. Start the candidate server, then Reconnect.";
        }
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if(IsRecording){Stop();return;}
        if(active!=null&&!active.Verifying)return;
        Start();
    }

    public void Start()
    {
        if(active!=null&&!active.Verifying)return;
        if(GlobalInput.TypeWhisperRunning()&&input.ShortcutEnabled){RecheckShortcut();return;}
        if(!Connected){_ = RefreshConnection();ShowSettings();return;}
        var snapshot=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Settings))!;
        var model=Models.FirstOrDefault(m=>m.Id==snapshot.PrimaryModel&&m.Installed);
        if(model==null){ShowSettings();return;}
        if(snapshot.VerifyAfterRecording&&!Models.Any(m=>m.Id==snapshot.VerificationModel&&ModelEligibility.CanVerify(m,snapshot.Language,model)&&m.Id!=model.Id))
        {ConnectionMessage="Choose an installed, runtime-tested verification model distinct from Dictation.";ShowSettings();Changed?.Invoke();return;}
        if(active?.Verifying==true)active.Cancel.Cancel();
        var destination=input.Epoch.Capture((long)WindowsTextDelivery.ForegroundWindow);
        bannerAnchor=(nint)destination.Window;
        var run=new Run(snapshot){Destination=destination};
        active=run;input.SessionActive=true;
        StateLabel="Preparing";DetailLabel="Opening the selected microphone…";hideAt=DateTimeOffset.MinValue;
        Changed?.Invoke();banner.Refresh();
        _=Record(run);
    }

    async Task Record(Run run)
    {
        Task? sender=null,receiver=null;
        try
        {
            run.Capture.Faulted+=message=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
            {if(active!=run)return;ConnectionMessage=message;run.NeedsReview=true;run.Cancel.Cancel();Changed?.Invoke();});
            run.Capture.LimitReached+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>
            {if(active!=run)return;run.Recording=false;StateLabel="Finishing · recording limit";Changed?.Invoke();});
            run.Capture.Start(run.Settings.MicrophoneId);
            run.Started=DateTimeOffset.Now;
            if(run.Settings.InsertAtCursor)
            {
                try
                {
                    if(!input.FocusTrackingAvailable)throw new InvalidOperationException("Focus tracking unavailable.");
                    var delivery=await automation.Run(token=>WindowsTextDelivery.Capture(token));
                    if(!input.Epoch.CanAdopt(run.Destination,(long)WindowsTextDelivery.ForegroundWindow))
                    {delivery.Invalidate();throw new InvalidOperationException("The initial destination changed.");}
                    run.Delivery=delivery;run.CanDeliver=true;
                }
                catch(Exception){run.Review.Reason="The original cursor could not be validated. Review before inserting.";run.NeedsReview=true;}
            }
            else {run.NeedsReview=true;run.Review.Reason="Review before inserting";}
            run.Cancel.Token.ThrowIfCancellationRequested();
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(run.Cancel.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                await run.Stream.Connect(run.Settings,timeout.Token);
            }
            if(active==run)StateLabel="Listening";
            receiver=run.Stream.Receive(e=>Handle(run,e),run.Cancel.Token);
            sender=SendAudio(run);
            var first=await Task.WhenAny(receiver,sender);
            await first;
            if(first==receiver)
            {
                run.Cancel.Cancel();run.Capture.Stop();
                try {await sender;}catch(OperationCanceledException){}
            }
            else await receiver;
            if(!run.Completed)throw new IOException("The API connection ended before session completion.");
        }
        catch(Exception ex)
        {
            run.NeedsReview=true;
            run.Review.Reason=ex is OperationCanceledException
                ?"Recording or verification stopped. Received text is preserved; some text may already be inserted."
                :"Transcription was interrupted. Received text is preserved; check your destination before pasting.";
            if(active==run)
            {
                StateLabel="Unavailable";ConnectionMessage="Recording stopped. Check the API, microphone and pending Review.";
            }
        }
        finally
        {
            run.Delivery?.Invalidate();run.Recording=false;run.Cancel.Cancel();run.Capture.Stop();
            if(sender!=null)try{await sender;}catch(Exception){}
            if(receiver!=null)try{await receiver;}catch(Exception){}
            await run.Stream.DisposeAsync();
            await run.Capture.DisposeAsync();
            if(run.Text.Length>0)
            {
                if(run.NeedsReview)SaveReview(run.Review);
                else DismissReview(run.Review.Id);
            }
            if(active==run)
            {
                active=null;input.SessionActive=false;hideAt=DateTimeOffset.Now.AddSeconds(6);
                if(StateLabel!="Unavailable")StateLabel=run.NeedsReview?"Review ready":"Inserted";
                DetailLabel=run.NeedsReview?"Open Review to choose or copy the wording.":"Dictation completed.";
                if(run.NeedsReview&&run.Text.Length>0)OpenReview(run.Review,false);
                Changed?.Invoke();banner.Refresh();
            }
            run.Cancel.Dispose();
        }
    }

    async Task SendAudio(Run run)
    {
        await foreach(var frame in run.Capture.Frames.Reader.ReadAllAsync(run.Cancel.Token))
        {
            try {await run.Stream.SendAudio(frame,run.Cancel.Token);}
            finally {Array.Clear(frame);}
        }
        await run.Stream.Finish(run.Cancel.Token);
    }

    async Task Handle(Run run,JsonElement e)
    {
        var type=e.GetProperty("type").GetString();
        if(type=="session.started")
        {
            string? Optional(string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
            var time=DateTimeOffset.TryParse(Optional("session_started_utc"),out var started)?started:(DateTimeOffset?)null;
            run.Review.AttachProvenance(new(e.GetProperty("session_id").GetString()!,
                new(e.GetProperty("model").GetString()!,Optional("model_revision")),
                run.Settings.VerifyAfterRecording?new(run.Settings.VerificationModel!,Optional("verification_revision")):null,time));
        }
        else if(type=="recording.limit_reached")
        {
            run.Recording=false;run.Capture.Stop();
            if(active==run)StateLabel="Finishing · recording limit";
        }
        else if(type is "transcript.updated" or "transcript.final")
        {
            run.Text=e.GetProperty("text").GetString()??"";
            run.Review.SetPrimary(run.Text);
            SaveReview(run.Review);
            if(run.CanDeliver&&run.Delivery!=null)
            {
                bool inserted;
                try {inserted=await automation.Run(token=>run.Delivery.Apply(run.Text,token));}
                catch(Exception){inserted=false;}
                if(!inserted)
                {
                    run.CanDeliver=false;run.NeedsReview=true;
                    run.Review.Reason=run.Delivery.Reason.Length>0?run.Delivery.Reason:
                        "The cursor changed or could not be checked. Your complete text is preserved.";
                    SaveReview(run.Review);
                }
            }
            if(type=="transcript.final"){run.PrimaryFinal=true;run.Recording=false;run.Capture.Stop();}
        }
        else if(type=="verification.started")
        {
            run.Verifying=true;
            if(active==run)StateLabel="Verifying";
        }
        else if(type=="verification.completed")
        {
            if(run.Review.Provenance is {} provenance&&e.TryGetProperty("comparison_id",out var identity))
                run.Review.AttachProvenance(provenance with {ComparisonId=identity.GetString()});
            run.Review.Verification=e.GetProperty("verification").GetString()??"";
            run.Review.VerificationCompleted=true;
            run.Review.Pieces=e.GetProperty("pieces").Deserialize<List<ReviewPiece>>(Protocol.Json)??[];
            run.Review.Reason=run.CanDeliver
                ?"Verification found alternative wording. The dictation is already in your destination; apply reviewed text deliberately."
                :"Choose the wording you heard, then copy it to your destination.";
            if(run.Review.Pieces.Any(p=>p.Different))run.NeedsReview=true;
            SaveReview(run.Review);
        }
        else if(type is "verification.failed" or "verification.cancelled")
        {
            run.NeedsReview=true;run.Review.Reason="Verification did not finish. Your original dictation is preserved.";
            SaveReview(run.Review);
        }
        else if(type=="session.completed")run.Completed=true;
        else if(type=="session.cancelled"){run.Completed=true;run.NeedsReview=true;}
        else if(type=="session.error")throw new IOException(e.GetProperty("message").GetString());
        if(active==run){Changed?.Invoke();banner.Refresh();}
    }

    public void Stop()
    {
        if(active==null||!active.Recording)return;
        active.Recording=false;StateLabel="Finishing";
        active.Capture.Stop();Changed?.Invoke();
    }
    public void Cancel()
    {
        if(active==null){hideAt=DateTimeOffset.MinValue;banner.Refresh();return;}
        active.NeedsReview=true;active.Delivery?.Invalidate();active.Cancel.Cancel();active.Capture.Stop();
    }
    public void EmergencyReview()
    {
        if(active!=null)
        {
            active.NeedsReview=true;active.Delivery?.Invalidate();
            if(active.Text.Length>0)SaveReview(active.Review);
            active.Cancel.Cancel();
        }
    }
    public void SaveReview(ReviewDocument document)
    {
        if(document.Primary.Length==0&&document.Verification.Length==0)return;
        if(!pending.Save(document))
            ConnectionMessage="Protected saving failed. Review is in memory only: copy it before exiting.";
    }
    public void DismissReview(string id)
    {
        pending.Dismiss(id);Changed?.Invoke();
    }
    public void OpenReview(ReviewDocument document,bool activate=true)
    {
        if(reviewWindows.TryGetValue(document.Id,out var open)){if(activate)open.Activate();return;}
        var window=new ReviewWindow(document,SaveReview,DismissReview){ShowActivated=activate};
        reviewWindows[document.Id]=window;window.Closed+=(_,_)=>reviewWindows.Remove(document.Id);
        window.Show();if(activate)window.Activate();
    }
    public void OpenLatestReview()
    {
        if(Pending.FirstOrDefault() is {} document)OpenReview(document);
        else ShowSettings();
    }
    public void ShowSettings()
    {
        if(settingsWindow!=null){settingsWindow.Show();settingsWindow.Activate();return;}
        settingsWindow=new SettingsWindow(this);
        settingsWindow.Closed+=(_,_)=>settingsWindow=null;
        settingsWindow.Show();settingsWindow.Activate();
    }
    void Exit()
    {
        if(IsBusy&&MessageBox.Show("Stop this session and exit? Received text will remain in Review. Temporary audio will be released.",
            "Exit Local Dictation",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        EmergencyReview();active?.Cancel.Cancel();
        if(pending.UnsavedIds.Count>0)
        {
            MessageBox.Show("Some Review text is only in memory because protected saving failed. Copy or explicitly dismiss it before exiting.","Unsaved Review");
            OpenLatestReview();return;
        }
        System.Windows.Application.Current.Shutdown();
    }
    public void Dispose()
    {
        timer.Stop();input.Dispose();tray.Visible=false;tray.Dispose();active?.Cancel.Cancel();automation.Dispose();
    }
}
