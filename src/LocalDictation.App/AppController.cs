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
        public ITextDelivery? Delivery;
        public DestinationStamp Destination;
        public bool CanDeliver,NeedsReview,PrimaryFinal,Completed,NormalCompletion,AutoInsertBlocked,Verifying,Closing,Recording=true;
        public DateTimeOffset Started=DateTimeOffset.Now;
        public string Text="";
        public string Stage="microphone",Failure="";
    }

    readonly ReviewStore store=new(Path.Combine(AppPaths.Data,"review"));
    readonly PendingReviews pending;
    readonly HistoryStore historyStore=new(Path.Combine(AppPaths.Data,"history"));
    public IReadOnlyList<HistoryEntry> History {get;private set;}=[];
    HistoryWindow? historyWindow;
    readonly DispatcherTimer reconnectTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    bool refreshing;
    ReviewWindow? reviewWindow;
    readonly Dictionary<string,ITextDelivery> destinations=[];
    readonly HashSet<string> inserting=[];
    readonly Dictionary<string,ReviewDocument> canonicalReviews=[];
    readonly HashSet<Task> recordingTasks=[];
    bool exiting;
    ReviewDocument? placementReview;
    CompactReviewProgress? bannerProgress;
    string? applyingBannerId;
    public ReviewDocument? BannerReview=>bannerProgress?.Document;
    public bool IsReviewUnsaved(string id)=>pending.UnsavedIds.Contains(id);
    public string? SelectedReviewPieceId=>bannerProgress?.SelectedId;
    public bool IsApplyingReview=>applyingBannerId!=null||BannerReview is {} doc&&inserting.Contains(doc.Id);
    readonly DispatcherTimer resumeTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    readonly SemaphoreSlim deliveryGate=new(1,1);
    bool deliveryBusy;
    readonly AutomationWorker automation=new();
    readonly GlobalInput input;
    readonly Forms.NotifyIcon tray;
    readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(80)};
    readonly BannerWindow banner;
    readonly bool noHotkey;
    SettingsWindow? settingsWindow;
    Run? active;
    string lastTranscript="",lastDeliveryMessage="";
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
    public string LatestText => active?.Text??BannerReview?.OutputText??lastTranscript;
    public bool LiveTranscription=>active?.Settings.Live??Settings.Live;
    public string DeliveryMessage=>active is {} run
        ?run.CanDeliver?"Text is also inserted at the original cursor.":run.Review.Reason
        :BannerReview?.Reason??lastDeliveryMessage;
    public float Level => active?.Capture.Level??0;
    public bool IsRecording => active?.Recording==true;
    public bool AtCursor=>active?.CanDeliver==true;
    public bool IsBusy => active!=null;
    public bool ShowBanner => IsBusy||BannerReview!=null||DateTimeOffset.Now<hideAt;
    public string MicrophoneLabel=>active?.Capture.DeviceName??"Microphone";
    public string ModelLabel=>Models.FirstOrDefault(m=>m.Id==(active?.Settings.PrimaryModel??Settings.PrimaryModel))?.Name??Settings.PrimaryModel;
    public string ElapsedLabel=>active is {} run?TimeSpan.FromSeconds(run.Capture.CapturedBytes/32000.0).ToString(@"mm\:ss"):"";
    public event Action? Changed;

    public AppController(bool noHotkey)
    {
        this.noHotkey=noHotkey;
        Settings=Settings.Load();
        Settings.ServerUri();
        ThemeManager.Apply(Settings.Theme);
        History=historyStore.Load();
        pending=new PendingReviews(store);
        foreach(var entry in History)canonicalReviews[entry.Id]=entry.Document;
        foreach(var document in pending.Documents.ToArray())
        {
            // A durable receipt in either store wins over a stale uninserted snapshot.
            if(canonicalReviews.TryGetValue(document.Id,out var saved)&&saved.InsertedAt!=null&&document.InsertedAt==null)
                SaveReview(saved);
            else canonicalReviews[document.Id]=document;
        }
        if(store.Errors.Count>0)ConnectionMessage=$"{store.Errors.Count} protected Review item(s) could not be read. Files were preserved.";
        input=new GlobalInput();
        input.UserEdited+=DestinationChanged;
        input.Cancel+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(Cancel);
        input.Toggle+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(Toggle);
        RecheckShortcut();
        banner=new BannerWindow(this);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Start / stop dictation",null,(_,_)=>Toggle());
        menu.Items.Add("Settings",null,(_,_)=>ShowSettings());
        menu.Items.Add("Pending Review",null,(_,_)=>OpenLatestReview());
        menu.Items.Add("History",null,(_,_)=>ShowHistory());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit",null,(_,_)=>Exit());
        tray=new Forms.NotifyIcon {Text="Local Dictation",Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application,
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
        reconnectTimer.Tick+=async(_,_)=>{if(!IsBusy&&!Connected)await RefreshConnection();};
        reconnectTimer.Start();
        resumeTimer.Tick+=async(_,_)=>
        {
            if(active is {Recording:true,CanDeliver:false,Delivery:{} target} run&&run.Settings.InsertAtCursor&&!target.IsConflict&&
                WindowsTextDelivery.ForegroundWindow==target.Window)await Deliver(run);
        };
        resumeTimer.Start();
    }

    public void SetRecordingControlWindow(nint window)=>input.NonActivatingWindow=window;
    void DestinationChanged(DestinationInput change)
    {
        var run=active;
        foreach(var target in destinations.Values.Distinct())
            if(!change.FocusOnly||!target.OwnsProgrammaticFocus(change.RootWindow))target.Suspend();
        if(run==null)return;
        if(change.FocusOnly&&run.Delivery?.OwnsProgrammaticFocus(change.RootWindow)==true)return;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
        {
            if(active!=run||!run.CanDeliver)return;
            run.CanDeliver=false;run.NeedsReview=true;run.Review.Reason=change.Reason;
            SaveReview(run.Review);Changed?.Invoke();banner.Refresh();
        });
    }

    async Task Deliver(Run run,bool wait=false)
    {
        if(!run.Settings.InsertAtCursor||run.Delivery==null||run.PrimaryFinal&&run.Settings.VerifyAfterRecording)return;
        if(wait)await deliveryGate.WaitAsync();
        else if(!await deliveryGate.WaitAsync(0))return;
        deliveryBusy=true;
        try
        {
            var text=run.Text;
            var inserted=await automation.Run(token=>run.Delivery.Apply(text,token));
            if(active!=run)return;
            run.CanDeliver=inserted;
            if(inserted)
            {
                run.NeedsReview=run.Settings.VerifyAfterRecording;
                run.Review.Reason="Live dictation resumed at the original field.";
            }
            else
            {
                run.NeedsReview=true;run.Review.Reason=run.Delivery.Reason;
                SaveReview(run.Review);
            }
        }
        catch(Exception){run.CanDeliver=false;run.NeedsReview=true;run.Review.Reason="Cursor checking is unavailable. Your transcript is preserved in Review.";}
        finally{deliveryBusy=false;deliveryGate.Release();Changed?.Invoke();}
    }

    async Task WithdrawForReview(Run run,bool restoreFocus)
    {
        if(run.Delivery==null||run.Delivery.IsConflict||run.Text.Length==0)return;
        await deliveryGate.WaitAsync();
        deliveryBusy=true;
        try
        {
            if(active!=run)return;
            SaveReview(run.Review);
            if(pending.UnsavedIds.Contains(run.Review.Id))
            {run.Review.Reason="Protected saving failed. The original draft was left in place; keep Review open.";return;}
            if(await automation.Run(token=>run.Delivery.Apply("",token,restoreFocus)))
            {
                run.Review.DraftWithdrawn=true;run.CanDeliver=false;
                run.Review.Reason="The live draft is held here for checking. Choose the wording; the last choice returns it to the original cursor.";
                SaveReview(run.Review);
            }
        }
        catch(Exception){run.Review.Reason="The original draft could not be moved to Review. Accept will validate it before replacement.";}
        finally{deliveryBusy=false;deliveryGate.Release();}
    }

    async Task<ReviewInsertionResult> AcceptReview(ReviewDocument document)
    {
        document=CanonicalReview(document);
        if(document.InsertedAt!=null)return new(true,"This Review was already inserted.");
        if(!document.CanInsert)return new(false,"Resolve the remaining choices before inserting.");
        if(exiting||IsBusy||deliveryBusy||!inserting.Add(document.Id))return new(false,"Finish the current operation, then try Accept again.");
        try
        {
            Changed?.Invoke();
            if(!destinations.TryGetValue(document.Id,out var target))
                return new(false,"This Review has no active destination. Use Choose destination, click the field, then press "+Settings.Shortcut+".");
            SaveReview(document);
            if(pending.UnsavedIds.Contains(document.Id))return new(false,"Protected saving failed. Your choices are preserved in this window; fix storage before inserting.");
            bool inserted;
            try{inserted=await automation.Run(token=>target.Apply(document.OutputText,token,true));}
            catch(Exception){inserted=false;}
            if(!inserted)return new(false,target.Reason.Length>0?target.Reason:"The original destination could not be validated. Choose another destination.");
            // Persist the receipt before cleanup. A cleanup failure must never trigger another insertion.
            document.InsertedAt=DateTimeOffset.UtcNow;document.DraftWithdrawn=false;
            lastTranscript=document.OutputText;lastDeliveryMessage="Finished text was inserted at the original cursor.";
            SaveReview(document);
            if(BannerReview?.Id==document.Id)bannerProgress=null;
            if(pending.UnsavedIds.Contains(document.Id))
                ConnectionMessage="Text was inserted, but its receipt could not be saved. Pending Review is preserved; do not insert it again after restarting.";
            else
            {
                try{DismissReview(document.Id);}
                catch(Exception){ConnectionMessage="Text was inserted. Pending cleanup failed; this Review is marked as already inserted.";}
            }
            placementReview=null;StateLabel="Inserted";DetailLabel="Reviewed text inserted. Nothing was submitted.";
            hideAt=DateTimeOffset.Now.AddSeconds(4);Changed?.Invoke();
            return new(true,"Reviewed text inserted.");
        }
        finally{inserting.Remove(document.Id);Changed?.Invoke();}
    }

    void ShowBannerReview(ReviewDocument document)
    {
        placementReview=null;
        if(reviewWindow?.IsApplying==true||IsApplyingReview)return;
        reviewWindow?.Close();
        bannerProgress=new CompactReviewProgress(document);
        StateLabel="Review";DetailLabel="Choose the correct wording. The final choice inserts the completed text.";
        hideAt=DateTimeOffset.MinValue;
        Changed?.Invoke();banner.Refresh();
    }

    public void SelectBannerPiece(string id)
    {
        if(IsApplyingReview||bannerProgress==null)return;
        bannerProgress.Select(id);Changed?.Invoke();
    }

    public void ChooseBannerPiece(string kind)
    {
        if(IsApplyingReview||bannerProgress==null||BannerReview?.InsertedAt!=null)return;
        bannerProgress.Choose(kind);SaveReview(bannerProgress.Document);Changed?.Invoke();
        if(bannerProgress.IsComplete)_ = ApplyBannerReview();
    }

    public void EditBannerPiece(string text)
    {
        if(IsApplyingReview||bannerProgress==null||BannerReview?.InsertedAt!=null)return;
        bannerProgress.Choose("custom",text);SaveReview(bannerProgress.Document);Changed?.Invoke();
        if(bannerProgress.IsComplete)_ = ApplyBannerReview();
    }

    public void KeepBannerWording()
    {
        if(IsApplyingReview||bannerProgress==null||BannerReview?.InsertedAt!=null)return;
        bannerProgress.KeepRemaining();SaveReview(bannerProgress.Document);Changed?.Invoke();
        _ = ApplyBannerReview();
    }

    public async Task ApproveBannerEditedText()
    {
        if(IsBusy||IsApplyingReview||bannerProgress==null||
            BannerReview is not {} document||document.InsertedAt!=null||document.ManualFinalDraft is not {} text)return;
        if(text.Any(char.IsControl))
        {
            document.Reason="Remove line breaks or control characters before inserting. Your edited text is saved here.";
            SaveReview(document);Changed?.Invoke();return;
        }
        // Whole-text edits are authoritative. Only this explicit approval
        // resolves the remaining comparison choices and permits delivery.
        bannerProgress.KeepRemaining();
        SaveReview(document);Changed?.Invoke();
        await ApplyBannerReview();
    }

    public async Task ApplyBannerReview()
    {
        if(IsApplyingReview||BannerReview is not {} document||!document.CanInsert)return;
        applyingBannerId=document.Id;
        try
        {
            Changed?.Invoke();
            var result=await AcceptReview(document);
            if(!result.Success&&BannerReview?.Id==document.Id)
            {document.Reason=result.Message;StateLabel="Review";SaveReview(document);}
        }
        finally
        {
            if(applyingBannerId==document.Id)applyingBannerId=null;
            Changed?.Invoke();banner.Refresh();
        }
    }

    public void ChooseBannerDestination()
    {if(BannerReview is {} document&&!IsApplyingReview)ChooseReviewDestination(document);}

    public void OpenBannerDetails()
    {
        if(BannerReview is not {} document||IsApplyingReview)return;
        bannerProgress=null;Changed?.Invoke();banner.Refresh();
        OpenReviewDetails(document);
    }

    void ChooseReviewDestination(ReviewDocument document)
    {
        if(IsBusy||!document.CanInsert){reviewWindow?.ShowStatus("Finish the current recording and resolve the choices before selecting a destination.");return;}
        if(IsApplyingReview)return;
        if(BannerReview?.Id==document.Id)bannerProgress=null;
        placementReview=document;reviewWindow?.Hide();
        lastTranscript=document.OutputText;
        lastDeliveryMessage="Click an empty cursor in your chosen field, then press "+Settings.Shortcut+". This inserts the reviewed text; it does not start recording.";
        StateLabel="Choose destination";DetailLabel=lastDeliveryMessage;hideAt=DateTimeOffset.Now.AddMinutes(2);
        Changed?.Invoke();banner.Refresh();
    }

    async Task InsertAtChosenDestination(ReviewDocument document)
    {
        if(deliveryBusy||inserting.Count>0)return;
        var stamp=input.Epoch.Capture((long)WindowsTextDelivery.ForegroundWindow);
        try
        {
            var target=await automation.Run(token=>WindowsTextDelivery.Capture(token));
            if(!input.Epoch.CanAdopt(stamp,(long)WindowsTextDelivery.ForegroundWindow))
            {target.Invalidate();throw new InvalidOperationException();}
            if(destinations.Remove(document.Id,out var previous))previous.Invalidate();
            destinations[document.Id]=target;
            var result=await AcceptReview(document);
            if(result.Success){reviewWindow?.Close();placementReview=null;}
            else{document.Reason=result.Message;OpenReview(document);}
        }
        catch(Exception)
        {document.Reason="That field could not be validated. Choose an editable destination and try again.";OpenReview(document);}
    }

    public void RecheckShortcut()
    {
        input.Shortcut=ShortcutSpec.Parse(Settings.Shortcut);
        var collision=GlobalInput.TypeWhisperRunning()&&Settings.Shortcut=="Ctrl+Shift";
        input.ShortcutEnabled=Settings.EnableShortcut&&!noHotkey&&!collision;
        ShortcutMessage=noHotkey?"Shortcut disabled for this test launch (--no-hotkey).":
            collision?"TypeWhisper is running. Quit it, then Recheck shortcut before using Ctrl+Shift here.":
            !Settings.EnableShortcut?"Shortcut disabled in Settings.":Settings.Shortcut+" starts and stops dictation.";
        Changed?.Invoke();
    }

    public void SaveSettings(Settings settings)
    {
        settings.Save();
        Settings=settings;
        ThemeManager.Apply(settings.Theme);
        RecheckShortcut();
    }

    public async Task RefreshConnection()
    {
        if(refreshing)return;refreshing=true;
        try
        {
            using var api=new ApiClient(Settings);
            var health=await api.Get("/health");
            if(!health.TryGetProperty("protocol_version",out var protocol)||protocol.GetInt32()!=1)
                throw new InvalidOperationException("This server supports batch transcription only. Start the Local Dictation session API.");
            Models=await api.Models();
            Connected=health.GetProperty("ready").GetBoolean();
            ConnectionMessage=Connected?(health.GetProperty("busy").GetBoolean()?"Another transcription is running":"Ready · Local transcription"):
                "API preparing or unavailable. Wait for runtime readiness, then Reconnect.";
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or ArgumentException)
        {
            Connected=false;
            ConnectionMessage=ex is InvalidOperationException?ex.Message:"Local transcription service is offline. Start the local API; reconnection is automatic.";
        }
        finally {refreshing=false;}
        Changed?.Invoke();
    }
    public async Task StartLocalApi()
    {
        if(Connected){await RefreshConnection();return;}
        try
        {
            var sourceRoot=typeof(AppController).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute),false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute=>attribute.Key=="LocalDictation.SourceRoot")?.Value;
            var script=string.IsNullOrWhiteSpace(sourceRoot)?null:Path.Combine(sourceRoot,"scripts","Start-CandidateServer.ps1");
            if(script is null||!File.Exists(script))
            {ConnectionMessage="This build's source checkout is unavailable. Rebuild from the current checkout, or start the Local Dictation API manually and Reconnect.";Changed?.Invoke();return;}
            var port=Settings.ServerUri().Port;
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",script,"-Port",port.ToString(System.Globalization.CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException();
            ConnectionMessage="Starting the local transcription service…";Changed?.Invoke();
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();await output;await error;
            if(process.ExitCode!=0)ConnectionMessage="Service did not start, or is already starting. Reconnect after a moment.";
            else ConnectionMessage="Local service is preparing its models. Reconnection is automatic.";
        }
        catch(Exception){ConnectionMessage="Could not start the local service. Check the local API launcher.";}
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if(exiting)return;
        if(IsRecording){Stop();return;}
        if(placementReview is {} placement){_ = InsertAtChosenDestination(placement);return;}
        if(active!=null&&!active.Verifying)return;
        Start();
    }

    public void ToggleReview(){if(IsRecording)Stop();else Start(true);}
    public void Start(bool reviewOnly=false)
    {
        if(exiting||reviewWindow?.IsApplying==true||IsApplyingReview||inserting.Count>0||deliveryBusy||active?.Closing==true)return;
        placementReview=null;
        if(active!=null&&!active.Verifying)return;
        if(GlobalInput.TypeWhisperRunning()&&Settings.Shortcut=="Ctrl+Shift"&&input.ShortcutEnabled){RecheckShortcut();return;}
        if(!Connected){_ = RefreshConnection();ShowSettings();return;}
        var snapshot=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Settings))!;
        if(reviewOnly)snapshot.InsertAtCursor=false;
        var model=Models.FirstOrDefault(m=>m.Id==snapshot.PrimaryModel&&m.Installed);
        if(model==null){ShowSettings();return;}
        if(snapshot.VerifyAfterRecording&&!Models.Any(m=>m.Id==snapshot.VerificationModel&&ModelEligibility.CanVerify(m,snapshot.Language,model)&&m.Id!=model.Id))
        {ConnectionMessage="Choose an installed, runtime-tested verification model distinct from Dictation.";ShowSettings();Changed?.Invoke();return;}
        if(active?.Verifying==true)active.Cancel.Cancel();
        var destination=input.Epoch.Capture((long)WindowsTextDelivery.ForegroundWindow);
        bannerAnchor=(nint)destination.Window;
        var run=new Run(snapshot){Destination=destination};
        bannerProgress=null;
        reviewWindow?.Close();
        lastTranscript=lastDeliveryMessage="";
        active=run;input.SessionActive=true;
        StateLabel="Preparing";DetailLabel="Opening the selected microphone…";hideAt=DateTimeOffset.MinValue;
        Changed?.Invoke();banner.Refresh();
        var recording=Record(run);
        recordingTasks.Add(recording);
        _=ObserveRecording(recording);
    }

    async Task ObserveRecording(Task recording)
    {
        try{await recording;}
        catch(Exception ex)
        {
            Diagnostics.Error("recording cleanup",ex);
            ConnectionMessage="Recording cleanup did not finish. Check pending Review before exiting.";
        }
        finally{recordingTasks.Remove(recording);}
    }

    async Task Record(Run run)
    {
        Task? sender=null,receiver=null;
        try
        {
            run.Capture.Faulted+=message=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
            {if(active!=run)return;ConnectionMessage=message;run.Failure=message;run.NeedsReview=true;run.Cancel.Cancel();Changed?.Invoke();});
            run.Capture.LimitReached+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>
            {if(active!=run)return;run.Recording=false;StateLabel="Finishing · recording limit";Changed?.Invoke();});
            run.Capture.Start(run.Settings.MicrophoneId);
            run.Started=DateTimeOffset.Now;
            run.Stage="destination";
            // Preview-only dictation still owns an original final destination.
            {
                try
                {
                    if(!input.FocusTrackingAvailable)throw new InvalidOperationException("Focus tracking unavailable.");
                    var delivery=await automation.Run(token=>WindowsTextDelivery.Capture(token));
                    if(!input.Epoch.CanAdopt(run.Destination,(long)WindowsTextDelivery.ForegroundWindow))
                    {delivery.Invalidate();throw new InvalidOperationException("The initial destination changed.");}
                    run.Delivery=delivery;destinations[run.Review.Id]=delivery;run.CanDeliver=run.Settings.InsertAtCursor;
                    if(!run.Settings.InsertAtCursor)
                    {run.NeedsReview=true;run.Review.Reason="Preview only while recording. The completed text will return to the original cursor.";}
                }
                catch(Exception ex){run.Review.Reason=ex is InvalidOperationException?ex.Message:
                    "The original cursor could not be validated. Dictation continues in this preview.";run.NeedsReview=true;}
            }
            run.Cancel.Token.ThrowIfCancellationRequested();
            run.Stage="connection";
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(run.Cancel.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                await run.Stream.Connect(run.Settings,timeout.Token);
            }
            if(active==run)StateLabel="Listening";
            run.Stage="audio or transcription";
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
            run.AutoInsertBlocked=true;
            var cancelled=ex is OperationCanceledException&&run.Cancel.IsCancellationRequested&&run.Failure.Length==0;
            if(!cancelled)Diagnostics.Error(run.Stage,ex);
            run.NeedsReview=true;
            run.Review.Reason=ex is OperationCanceledException
                ?"Recording or verification stopped. Received text is preserved; some text may already be inserted."
                :"Transcription was interrupted. Received text is preserved; check your destination before pasting.";
            if(active==run)
            {
                StateLabel=cancelled?"Stopped":"Unavailable";ConnectionMessage=cancelled?"Recording cancelled. Received text is preserved.":run.Failure.Length>0?run.Failure:
                    $"Recording stopped during {run.Stage} ({ex.GetType().Name}, 0x{ex.HResult:X8}). Retry or check the selected microphone.";
            }
        }
        finally
        {
            run.Closing=true;
            run.Recording=false;run.Cancel.Cancel();run.Capture.Stop();
            if(sender!=null)try{await sender;}catch(Exception){}
            if(receiver!=null)try{await receiver;}catch(Exception){}
            await run.Stream.DisposeAsync();
            await run.Capture.DisposeAsync();
            var audio=run.Capture.TakeAudio();
            try
            {
                if(run.Text.Length>0&&audio.Length>0)
                {
                    historyStore.Add(run.Review,audio);
                    History=historyStore.Load();
                }
            }
            catch(Exception)
            {
                run.NeedsReview=true;
                run.Review.Reason="History could not save this recording. The transcript is kept in pending Review; audio recovery is unavailable.";
                ConnectionMessage="History could not be saved. Check pending Review before exiting.";
            }
            finally {System.Security.Cryptography.CryptographicOperations.ZeroMemory(audio);}
            await CompleteRun(run);
            run.Cancel.Dispose();
        }
    }

    async Task CompleteRun(Run run)
    {
        if(run.Text.Length>0)SaveReview(run.Review);
        if(active!=run)return;
        if(run.Settings.VerifyAfterRecording&&run.Text.Length>0&&!run.Review.DraftWithdrawn&&!run.AutoInsertBlocked)
            await WithdrawForReview(run,true);
        if(active!=run)return;
        lastTranscript=run.Text;lastDeliveryMessage=run.Review.Reason;
        active=null;input.SessionActive=false;hideAt=DateTimeOffset.Now.AddSeconds(6);
        if(run.Text.Length==0)
        {
            if(StateLabel!="Unavailable")StateLabel="Stopped";
            DetailLabel="No transcript was returned. Check the microphone level and try again.";
            Changed?.Invoke();banner.Refresh();return;
        }
        ShowBannerReview(run.Review);
        var action=CompactCompletion.Decide(run.NormalCompletion&&!run.AutoInsertBlocked,run.PrimaryFinal,
            run.Settings.VerifyAfterRecording,run.Review.HasCompletedVerification,run.Review.UnresolvedCount);
        if(action==CompactCompletionAction.Insert&&destinations.ContainsKey(run.Review.Id))await ApplyBannerReview();
        else if(action==CompactCompletionAction.Insert)
        {run.Review.Reason="The text is ready. Choose destination to place it in your selected field.";Changed?.Invoke();}
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
            if(run.Delivery!=null)await Deliver(run,true);
            if(type=="transcript.final")
            {
                run.PrimaryFinal=true;run.Recording=false;run.Capture.Stop();
                if(run.Settings.VerifyAfterRecording)
                {run.NeedsReview=true;await WithdrawForReview(run,false);}
            }
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
            run.Review.Reason="Choose the correct wording. The next phrase opens automatically; the last choice inserts the finished text.";
            run.NeedsReview=true;
            SaveReview(run.Review);
        }
        else if(type is "verification.failed" or "verification.cancelled")
        {
            run.AutoInsertBlocked=true;
            run.NeedsReview=true;run.Review.Reason="Verification did not finish. Your original dictation is preserved.";
            SaveReview(run.Review);
        }
        else if(type=="session.completed"){run.Completed=true;run.NormalCompletion=true;}
        else if(type=="session.cancelled"){run.Completed=true;run.NeedsReview=true;run.AutoInsertBlocked=true;}
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
        if(IsApplyingReview||inserting.Count>0)return;
        if(active==null){bannerProgress=null;placementReview=null;hideAt=DateTimeOffset.MinValue;Changed?.Invoke();banner.Refresh();return;}
        active.AutoInsertBlocked=true;active.NeedsReview=true;active.Delivery?.Invalidate();active.Cancel.Cancel();active.Capture.Stop();
    }
    public void EmergencyReview()
    {
        if(active!=null)
        {
            active.AutoInsertBlocked=true;
            active.NeedsReview=true;active.Delivery?.Invalidate();
            if(active.Text.Length>0)SaveReview(active.Review);
            active.Cancel.Cancel();
        }
    }
    ReviewDocument CanonicalReview(ReviewDocument document)
    {
        if(canonicalReviews.TryGetValue(document.Id,out var current))
        {
            // Detached History/UI snapshots cannot replace current edits or erase a receipt.
            if(current.InsertedAt!=null||document.InsertedAt==null)return current;
        }
        canonicalReviews[document.Id]=document;
        return document;
    }

    public void SaveReview(ReviewDocument document)
    {
        document=CanonicalReview(document);
        if(document.Primary.Length==0&&document.Verification.Length==0)return;
        if(History.Any(e=>e.Id==document.Id))
        {
            try{historyStore.Update(document);History=historyStore.Load();}
            catch(Exception){ConnectionMessage="History changes could not be saved. Pending Review is kept separately.";}
        }
        if(!pending.Save(document))
            ConnectionMessage="Protected saving failed. Review is in memory only: copy it before exiting.";
    }
    public void DismissReview(string id)
    {
        pending.Dismiss(id);
        if(BannerReview?.Id==id)bannerProgress=null;
        if(destinations.Remove(id,out var target))target.Invalidate();
        Changed?.Invoke();
    }
    public void OpenReview(ReviewDocument document,bool activate=true)
    {
        if(IsBusy){ConnectionMessage="Finish recording before opening another Review.";Changed?.Invoke();return;}
        ShowBannerReview(CanonicalReview(document));
    }
    void OpenReviewDetails(ReviewDocument document,bool activate=true)
    {
        placementReview=null;
        if(StateLabel=="Choose destination")hideAt=DateTimeOffset.MinValue;
        if(reviewWindow is {} open)
        {
            if(open.IsApplying)return;
            if(open.ReviewId==document.Id){open.Show();open.ShowStatus(document.Reason);if(activate)open.Activate();return;}
            open.Close();
        }
        var window=new ReviewWindow(document,SaveReview,DismissReview,AcceptReview,ChooseReviewDestination){ShowActivated=activate};
        reviewWindow=window;window.Closed+=(_,_)=>{if(reviewWindow==window)reviewWindow=null;};
        window.Show();if(activate)window.Activate();
    }
    public void OpenLatestReview()
    {
        if(Pending.FirstOrDefault() is {} document)OpenReview(document);
        else ShowSettings();
    }
    public void ShowSettings()
    {
        placementReview=null;
        if(StateLabel=="Choose destination")hideAt=DateTimeOffset.MinValue;
        if(settingsWindow!=null){settingsWindow.Show();settingsWindow.Activate();return;}
        settingsWindow=new SettingsWindow(this);
        settingsWindow.Closed+=(_,_)=>settingsWindow=null;
        settingsWindow.Show();settingsWindow.Activate();
    }
    public byte[] HistoryAudio(string id)=>historyStore.ReadAudio(id);
    public void DeleteHistory(string id){historyStore.Delete(id);History=historyStore.Load();Changed?.Invoke();}
    public void ShowHistory()
    {
        if(historyWindow!=null){historyWindow.Show();historyWindow.Activate();return;}
        historyWindow=new HistoryWindow(this);historyWindow.Closed+=(_,_)=>historyWindow=null;
        historyWindow.Show();historyWindow.Activate();
    }
    async Task PrepareForExit()
    {
        exiting=true;
        EmergencyReview();active?.Capture.Stop();
        // Include older runs cancelled when a new recording superseded verification.
        await Task.WhenAll(recordingTasks.ToArray());
    }

    async void Exit()
    {
        if(exiting)return;
        if(IsBusy&&MessageBox.Show("Stop this session and exit? Received text and its original recording will be saved before closing.",
            "Exit Local Dictation",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        // The confirmation pumps the dispatcher; insertion may have started while it was open.
        if(IsApplyingReview||reviewWindow?.IsApplying==true||inserting.Count>0)
        {ConnectionMessage="Wait for the insertion check before exiting.";Changed?.Invoke();return;}
        try{await PrepareForExit();}
        catch(Exception)
        {exiting=false;ConnectionMessage="Recording cleanup did not finish. Check pending Review before exiting.";Changed?.Invoke();return;}
        if(pending.UnsavedIds.Count>0)
        {
            exiting=false;
            MessageBox.Show("Some Review text is only in memory because protected saving failed. Copy or explicitly dismiss it before exiting.","Unsaved Review");
            OpenLatestReview();return;
        }
        System.Windows.Application.Current.Shutdown();
    }
    public void Dispose()
    {
        timer.Stop();reconnectTimer.Stop();resumeTimer.Stop();input.Dispose();tray.Visible=false;tray.Dispose();active?.Cancel.Cancel();automation.Dispose();
    }
}
