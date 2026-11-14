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
        public ReviewDocument Review=new() {PrimaryModel=settings.PrimaryModel,RequestedLanguage=settings.Language,
            RequiresExplicitInsert=settings.AfterRecording!=AfterRecordingAction.InsertDirectly,
            VerificationModel=settings.VerifyAfterRecording?settings.VerificationModel:null,
            Reason="Interrupted recording. Some draft text may already be in your destination."};
        public CancellationTokenSource Cancel=new();
        public MicrophoneCapture Capture=new();
        public SessionStream Stream=new();
        public ITextDelivery? Delivery;
        public bool FinalOnlyDestination;
        public string? DestinationProcessName;
        public CompletionDestinationGate CompletionGate = new();
        public Task FinishDestinationCheck = Task.CompletedTask;
        public DestinationStamp FinishStamp;
        public DestinationStamp Destination;
        public bool CanDeliver,NeedsReview,PrimaryFinal,Completed,NormalCompletion,AutoInsertBlocked,Verifying,Closing,DestinationChecked,Recording=true;
        public DateTimeOffset Started=DateTimeOffset.Now;
        public string Text="";
        public string Stage="microphone",Failure="";
        public Settings? RetrySettings;
        public ReviewDocument? ReviewFallback;
        public long ReplayBytes;
        public TaskCompletionSource? LanguageSelection;
    }

    readonly ReviewStore store=new(Path.Combine(AppPaths.Data,"review"));
    readonly PendingReviews pending;
    readonly HistoryStore historyStore=new(Path.Combine(AppPaths.Data,"history"));
    public IReadOnlyList<HistoryEntry> History {get;private set;}=[];
    readonly DispatcherTimer reconnectTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    bool refreshing;
    ReviewWindow? reviewWindow;
    CompactReviewWindow? compactReviewWindow;
    ITextDelivery? lastDeliveryTarget;
    bool clipboardWasPending,checkingClipboard,drainingConfirmedPastes;
    string lastClipboardRecoveryMessage="";
    readonly Dictionary<string,ITextDelivery> destinations=[];
    readonly HashSet<string> inserting=[];
    readonly Dictionary<string,ReviewDocument> canonicalReviews=[];
    readonly HashSet<Task> recordingTasks=[];
    bool exiting,disposed,restoringClipboard;
    ReviewDocument? placementReview;
    Run? heldShortcutRun;
    CompactReviewProgress? bannerProgress;
    string? lastInsertionFailureId,lastInsertionFailure;
    public string BannerInsertionError=>BannerReview?.Id==lastInsertionFailureId?lastInsertionFailure??"":"";
    string? applyingBannerId;
    public ReviewDocument? BannerReview=>bannerProgress?.Document;
    public bool IsReviewUnsaved(string id)=>pending.UnsavedIds.Contains(id);
    public string? SelectedReviewPieceId=>bannerProgress?.SelectedId;
    public bool IsApplyingReview=>applyingBannerId!=null||BannerReview is {} doc&&inserting.Contains(doc.Id);
    readonly DispatcherTimer resumeTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    readonly SemaphoreSlim deliveryGate=new(1,1);
    bool deliveryBusy;
    readonly AutomationWorker automation=new();
    readonly Func<nint> insertionForegroundWindow=()=>WindowsTextDelivery.ForegroundWindow;
    readonly Func<CancellationToken,ITextDelivery> captureInsertionTarget=token=>WindowsTextDelivery.Capture(token);
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
    public IReadOnlyList<DictationItem> Dictations=>DictationLibrary.Merge(History,Pending,pending.UnsavedIds);
    public bool Connected {get;private set;}
    public string ConnectionMessage {get;private set;}="Checking the local API…";
    public string ShortcutMessage {get;private set;}="";
    public string StateLabel {get;private set;}="Ready";
    public string DetailLabel {get;private set;}="";
    public string LatestText => active?.Text??BannerReview?.OutputText??lastTranscript;
    public string VerificationPreviewText => active is {Recording:false,Settings.VerifyAfterRecording:true} run?run.Text:"";
    public bool IsVerifying => active?.Verifying==true;
    public bool LiveTranscription=>active?.Settings.Live??Settings.Live;
    public string DeliveryMessage=>ClipboardRecovery.HasPending?ClipboardRecovery.Message:active is {} run
        ?run.Settings.AfterRecording==AfterRecordingAction.PreviewFirst?"Preview first: review the result before choosing Insert.":
            run.FinalOnlyDestination&&!run.AutoInsertBlocked?"Finished text is inserted at the original cursor after recording.":
            run.CanDeliver?"Text is also inserted at the original cursor.":run.Review.Reason
        :BannerReview?.Reason??lastDeliveryMessage;
    public string RecordingDeliveryLabel=>active?.Settings.AfterRecording==AfterRecordingAction.PreviewFirst?"Preview first · Text stays here":
        active is {FinalOnlyDestination:true}?"Inserts after Finish":
        AtCursor?(LiveTranscription?"Live text · At your cursor":"After Finish · At your cursor"):"Review mode · Text stays here";
    public float Level => active?.Capture.Level??0;
    public bool IsRecording => active?.Recording==true;
    public bool AtCursor=>active?.CanDeliver==true;
    public bool IsBusy => active!=null;
    public bool HasClipboardRecovery=>ClipboardRecovery.HasPending;
    public bool ShowBanner => active is {DestinationChecked:true}||BannerReview is {InsertedAt:null}||DateTimeOffset.Now<hideAt;
    public string MicrophoneLabel=>active?.Capture.DeviceName??"Microphone";
    public string ModelLabel=>Models.FirstOrDefault(m=>m.Id==(active?.Settings.PrimaryModel??Settings.PrimaryModel))?.Name??Settings.PrimaryModel;
    string BannerModelId=>active is {Verifying:true,Settings.VerificationModel:{} verifier}?verifier:
        active?.Settings.PrimaryModel??(BannerReview is {PrimaryModel:{Length:>0}} review?review.PrimaryModel:Settings.PrimaryModel);
    public string BannerModelLabel=>BannerModelId switch
    {
        "kinit/whisper-large-v3-turbo-sk"=>"KInIT Turbo SK",
        "openai/whisper-large-v3-turbo"=>"OpenAI Turbo",
        "openai/whisper-large-v3"=>"OpenAI Large-v3",
        "openai/whisper-tiny"=>"Whisper Tiny",
        _=>Models.FirstOrDefault(m=>m.Id==BannerModelId)?.Name??BannerModelId.Split('/').Last()
    };
    public string BannerModelDetails=>(IsVerifying?"Verification":"Dictation")+" model: "+
        (Models.FirstOrDefault(m=>m.Id==BannerModelId)?.Name??BannerModelId);
    public string ElapsedLabel=>active is {} run?TimeSpan.FromSeconds((run.ReplayBytes>0?run.ReplayBytes:run.Capture.CapturedBytes)/32000.0).ToString(@"mm\:ss"):"";
    public string InputLanguageLabel
    {
        get
        {
            var doc=active?.Review??BannerReview;
            var settings=active?.Settings??Settings;
            var model=Models.FirstOrDefault(m=>m.Id==(doc?.PrimaryModel??settings.PrimaryModel));
            var label=LanguagePolicy.Label(doc?.RequestedLanguage is {Length:>0} requested?requested:settings.Language,doc?.Language,doc?.LanguageMode,model);
            return active?.RetrySettings is {} retry?label+" → "+retry.Language.ToUpperInvariant():label;
        }
    }
    public bool CanChangeInputLanguage=>active is {Closing:false}||BannerReview is {} doc&&History.Any(e=>e.Id==doc.Id);
    LanguageWindow? languageWindow;
    public void ShowInputLanguage(ReviewDocument? document=null)
    {
        if(languageWindow!=null){languageWindow.Activate();return;}
        var run=document==null?active:null;
        var source=document??run?.Review??BannerReview;
        if(source==null||run==null&&!History.Any(e=>e.Id==source.Id))return;
        if(run!=null)run.LanguageSelection=new(TaskCreationOptions.RunContinuationsAsynchronously);
        languageWindow=new LanguageWindow(this,source.PrimaryModel,run?.Recording==true,
            code=>ChangeInputLanguage(code,run,source),source.RequestedLanguage.Length>0?source.RequestedLanguage:Settings.Language,
            run?.Settings.PreferredLanguages??Settings.PreferredLanguages);
        languageWindow.Closed+=(_,_)=>{languageWindow=null;run?.LanguageSelection?.TrySetResult();};
        languageWindow.Show();
    }
    Settings LanguageRetrySettings(string currentModel,string code)
    {
        var selected=LanguagePolicy.RetryModel(Models,currentModel,code)??throw new ArgumentException("No installed model supports this language.");
        var value=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(active?.Settings??Settings))!;
        value.Language=code;value.PrimaryModel=selected.Id;value.Live=false;
        LanguagePolicy.ValidateForModel(selected,code,value.PreferredLanguages);
        if(value.VerifyAfterRecording)
        {
            var choices=Models.Where(m=>m.Id!=selected.Id&&ModelEligibility.CanVerify(m,code,selected,value.PreferredLanguages)).ToArray();
            var verifier=choices.FirstOrDefault(m=>m.Id==value.VerificationModel)??choices.FirstOrDefault();
            if(verifier==null)throw new ArgumentException("No distinct verification model supports this language. Change verification in Settings first.");
            value.VerificationModel=verifier.Id;
        }
        return value;
    }
    string? ChangeInputLanguage(string code,Run? expected,ReviewDocument source)
    {
        try
        {
            if(expected!=null)
            {
                if(active!=expected)return "This dictation has finished. Change its language from History.";
                expected.RetrySettings=LanguageRetrySettings(expected.Settings.PrimaryModel,code);
                if(!expected.Recording&&expected.PrimaryFinal)expected.Cancel.Cancel();
                Changed?.Invoke();return null;
            }
            if(IsBusy||IsApplyingReview||ClipboardRecovery.HasPending)return "Finish the current operation before retranscribing a saved recording.";
            var settings=LanguageRetrySettings(source.PrimaryModel,code);
            byte[]? wave=null;
            try
            {
                wave=HistoryAudio(source.Id);
                var pcm=RecordedAudio.PcmFromWave(wave);
                var run=new Run(settings){Recording=false,DestinationChecked=true,AutoInsertBlocked=true,ReplayBytes=pcm.Length};
                run.Review.RequiresExplicitInsert=true;
                run.Review.RetranscriptionOf=source.Id;
                run.Review.Reason="Retranscribed from the original recording. Choose destination to insert this new version.";
                active=run;input.SessionActive=true;bannerProgress=null;hideAt=DateTimeOffset.MinValue;
                StateLabel="Retranscribing";
                TrackRecording(Record(run,pcm));Changed?.Invoke();return null;
            }
            finally{if(wave!=null)System.Security.Cryptography.CryptographicOperations.ZeroMemory(wave);}
        }
        catch(Exception error) when(error is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {return error.Message;}
    }
    void TrackRecording(Task recording)
    {
        recordingTasks.Add(recording);_=ObserveRecording(recording);
    }
    bool TransferReplayDestination(ReviewDocument previous,ReviewDocument next,ITextDelivery target)
    {
        // Consume any earlier proven insertion before handing over its carrier.
        DrainConfirmedPastes();
        foreach(var alias in destinations.Where(e=>ReferenceEquals(e.Value,target)).Select(e=>e.Key).ToArray())destinations.Remove(alias);
        if(previous.InsertedAt!=null||target.IsConflict)return false;
        destinations[next.Id]=target;return true;
    }
    async Task ReplayAtOriginalDestination(Run previous,byte[] pcm)
    {
        var settings=previous.RetrySettings!;
        var run=new Run(settings){Recording=false,DestinationChecked=true,ReplayBytes=pcm.Length,
            Delivery=previous.Delivery,Destination=previous.Destination,DestinationProcessName=previous.DestinationProcessName,
            FinalOnlyDestination=previous.FinalOnlyDestination,CompletionGate=previous.CompletionGate,
            FinishDestinationCheck=previous.FinishDestinationCheck,FinishStamp=previous.FinishStamp,
            AutoInsertBlocked=previous.AutoInsertBlocked,
            ReviewFallback=previous.ReviewFallback==null?null:
                previous.Review.Primary.Length>0||previous.Review.ManualFinalDraft!=null?previous.Review:previous.ReviewFallback};
        run.Review.RetranscriptionOf=previous.Review.Id;
        run.Review.RequiresExplicitInsert|=previous.Review.RequiresExplicitInsert;
        run.Review.DraftWithdrawn=previous.Review.DraftWithdrawn;
        if(run.Delivery!=null&&!TransferReplayDestination(previous.Review,run.Review,run.Delivery))
        {run.Delivery=null;run.AutoInsertBlocked=true;}
        BindTranscribingReview(previous.Review,run.Review);
        active=run;StateLabel="Retranscribing";
        if(History.Any(e=>e.Id==previous.Review.Id))
        {
            // The source remains in protected History; it needs no duplicate pending action.
            try{pending.Dismiss(previous.Review.Id);}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){Diagnostics.Error("source Review cleanup",error);}
        }
        Changed?.Invoke();
        // Record owns and clears this private replay copy. Original history stays protected.
        await Record(run,pcm.ToArray());
    }

    string VerificationLanguage(ReviewDocument document)=>document.Language is {Length:>0} actual?actual:
        document.RequestedLanguage.Length>0?document.RequestedLanguage:Settings.Language;
    public List<ModelInfo> VerificationChoices(ReviewDocument document)
    {
        var language=VerificationLanguage(document);
        var primary=Models.FirstOrDefault(m=>m.Id==document.PrimaryModel)??new ModelInfo{Id=document.PrimaryModel,Languages=language=="auto"?new[]{"auto"}.Concat(Settings.PreferredLanguages.Count>0?Settings.PreferredLanguages:LanguagePolicy.Available(Models).Select(l=>l.Code)).ToArray():[language]};
        return Models.Where(m=>m.Id!=document.PrimaryModel&&m.Compatible&&ModelEligibility.CanVerify(m,language,primary,Settings.PreferredLanguages)).ToList();
    }
    public bool CanVerifyReview(ReviewDocument document)=>!IsBusy&&!IsApplyingReview&&!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction&&document.Primary.Length>0&&
        History.Any(e=>e.Id==document.Id)&&VerificationChoices(document).Count>0;
    public string? StartSavedVerification(ReviewDocument document,ModelInfo verifier)
    {
        if(!CanVerifyReview(document)||!VerificationChoices(document).Any(m=>m.Id==verifier.Id))return "The recording or second model is no longer available.";
        byte[]? wave=null,pcm=null;
        try
        {
            document=CanonicalReview(document);
            wave=HistoryAudio(document.Id);pcm=RecordedAudio.PcmFromWave(wave);
            var source=JsonSerializer.Deserialize<ReviewDocument>(JsonSerializer.Serialize(document))!;
            var settings=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Settings))!;
            settings.PrimaryModel=source.PrimaryModel;settings.VerificationModel=verifier.Id;settings.VerifyAfterRecording=true;settings.Live=false;
            settings.Language=VerificationLanguage(source);
            var run=new Run(settings){Review=SavedVerification.Begin(source,verifier.Id),Recording=false,Verifying=true,
                DestinationChecked=true,PrimaryFinal=true,AutoInsertBlocked=true,ReplayBytes=pcm.Length,Text=source.Primary,ReviewFallback=source};
            run.CompletionGate.BeginFinish();run.CompletionGate.ConfirmOriginalField(false);
            // Keep the existing Review surface. Its model picker is inline, and
            // the result replaces its content without another compact popup.
            BindTranscribingReview(source,run.Review);
            bannerProgress=new CompactReviewProgress(run.Review);placementReview=null;
            active=run;input.SessionActive=true;StateLabel="Verifying";hideAt=DateTimeOffset.MinValue;
            TrackRecording(VerifySavedRecording(run,source,wave,pcm));wave=pcm=null;
            Changed?.Invoke();banner.Refresh();return null;
        }
        catch(Exception error) when(error is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {return "The original recording could not be opened. "+error.Message;}
        finally
        {
            if(wave!=null)System.Security.Cryptography.CryptographicOperations.ZeroMemory(wave);
            if(pcm!=null)System.Security.Cryptography.CryptographicOperations.ZeroMemory(pcm);
        }
    }
    async Task VerifySavedRecording(Run run,ReviewDocument source,byte[] wave,byte[] pcm)
    {
        try
        {
            run.Stage="second transcript";
            using var api=new ApiClient(run.Settings,TimeSpan.FromMinutes(11));
            await api.WaitForIdle(run.Cancel.Token);
            var result=await api.VerifyRecording(wave,source,run.Settings.VerificationModel!,run.Settings.Language,run.Settings.PreferredLanguages,run.Cancel.Token);
            run.Cancel.Token.ThrowIfCancellationRequested();
            SavedVerification.Complete(run.Review,source,result);run.Completed=true;run.NormalCompletion=true;
        }
        catch(OperationCanceledException) when(run.Cancel.IsCancellationRequested)
        {run.Review.Reason="Second-model verification stopped. The original transcript and recording are preserved.";}
        catch(Exception error)
        {Diagnostics.Error("saved recording verification",error);run.Review.Reason="The second transcript could not finish. The original text and recording are preserved.";}
        finally
        {
            run.Closing=true;run.Cancel.Cancel();
            try
            {
                historyStore.Add(run.Review,pcm);History=historyStore.Load();SaveReview(run.Review);
            }
            catch(Exception error)
            {Diagnostics.Error("saved verification history",error);SaveReview(run.Review);ConnectionMessage="The comparison is kept in Review; its audio copy could not be saved.";}
            try{await run.Stream.DisposeAsync();await run.Capture.DisposeAsync();}
            finally
            {
                try
                {
                    if(run.LanguageSelection is {} selection)await selection.Task;
                    if(active==run&&run.RetrySettings!=null&&!exiting)await ReplayAtOriginalDestination(run,pcm);
                    else if(active==run)
                    {
                        active=null;input.SessionActive=false;lastTranscript=run.Text;
                        if(!exiting)ShowBannerReview(run.Review,true);
                        Changed?.Invoke();banner.Refresh();
                    }
                }
                finally
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(wave);System.Security.Cryptography.CryptographicOperations.ZeroMemory(pcm);
                    run.Cancel.Dispose();
                }
            }
        }
    }

    public event Action? Changed;

    public AppController(bool noHotkey)
    {
        this.noHotkey=noHotkey;
        Settings=Settings.Load();
        Settings.FinalOnlyApplications ??= [];
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
        input.HoldStart+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(StartHeldShortcut);
        input.HoldStop+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(StopHeldShortcut);
        RecheckShortcut();
        banner=new BannerWindow(this);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Start / stop dictation",null,(_,_)=>Toggle());
        menu.Items.Add("Settings",null,(_,_)=>ShowSettings());
        menu.Items.Add("History",null,(_,_)=>ShowHistory());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit",null,(_,_)=>Exit());
        tray=new Forms.NotifyIcon {Text="Local Dictation",Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application,
            ContextMenuStrip=menu,Visible=true};
        tray.DoubleClick+=(_,_)=>ShowSettings();
        ClipboardRecovery.Changed+=ClipboardRecoveryChanged;
        timer.Tick+=(_,_) =>
        {
            // Neither showing nor hiding our UI may change focus before the
            // initial external element and cursor have been captured.
            if(active is {DestinationChecked:false})return;
            if(active!=null)
            {
                var elapsed=TimeSpan.FromSeconds(active.Capture.CapturedBytes/32000.0);
                DetailLabel=$"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00} · {Models.FirstOrDefault(m=>m.Id==active.Settings.PrimaryModel)?.Name??active.Settings.PrimaryModel}"+
                    (active.Settings.AfterRecording==AfterRecordingAction.PreviewFirst?" · Preview first":active.FinalOnlyDestination?" · Inserts after Finish":active.CanDeliver?" · At cursor":" · Paused")+
                    " · "+active.Capture.DeviceName+(active.Recording?" · Mic active":" · Mic stopped");
            }
            banner.Refresh();
        };
        timer.Start();
        reconnectTimer.Tick+=async(_,_)=>{if(!IsBusy&&!Connected)await RefreshConnection();};
        reconnectTimer.Start();
        resumeTimer.Tick+=async(_,_)=>
        {
            DrainConfirmedPastes();
            // Restoration must not depend on a responsive UIA provider. These
            // operations have separate reservations and run independently.
            _=RetryClipboardRestorationAsync();
            await RecheckPendingInsertionAsync();
            if(ClipboardRecovery.HasPending)return;
            if(active is {Recording:true,CanDeliver:false,Delivery:{} target} run&&run.Settings.CanInsertLive&&!target.IsConflict&&
                WindowsTextDelivery.ForegroundWindow==target.Window)await Deliver(run);
        };
        resumeTimer.Start();
    }

    void ClipboardRecoveryChanged()
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
        {
            if(disposed)return;
            if(ClipboardRecovery.HasPending)ShowClipboardRecovery();
            else{if(!ClipboardRecovery.HasPending)ClearClipboardRecoveryStatus();Changed?.Invoke();banner.Refresh();}
        });
    }

    void ShowClipboardRecovery()
    {
        var message=ClipboardRecovery.Message;
        clipboardWasPending=true;lastClipboardRecoveryMessage=message;
        ConnectionMessage=message;
        Changed?.Invoke();banner.Refresh();
    }

    void ClearClipboardRecoveryStatus()
    {
        if(!clipboardWasPending)return;
        clipboardWasPending=false;
        if(ConnectionMessage==lastClipboardRecoveryMessage)
            ConnectionMessage=Connected?"Ready · Local transcription":"Local transcription service is offline.";
    }

    void DrainConfirmedPastes()
    {
        if(drainingConfirmedPastes)return;
        drainingConfirmedPastes=true;
        try
        {
            foreach(var entry in destinations.ToArray())
            {
                if(entry.Value is not WindowsTextDelivery target)continue;
                while(target.ConfirmedPendingPaste is {} confirmation)
                {
                    lastDeliveryTarget=target;
                    if(confirmation.IsFinal&&canonicalReviews.TryGetValue(entry.Key,out var document)&&document.InsertedAt==null)
                    {
                        if(document.CanInsert&&document.OutputText==confirmation.Text)
                            RecordSuccessfulInsertion(document,"The original insertion was confirmed. Nothing was sent.");
                        else
                        {
                            document.Reason="An earlier version was inserted. Your later edits are kept for explicit replacement.";
                            SaveReview(document);
                        }
                    }
                    // Drain every proven fact before removal or clipboard restoration.
                    target.ClearConfirmedPendingPaste(confirmation);
                }
            }
        }
        finally{drainingConfirmedPastes=false;}
    }

    public async Task RecheckPendingInsertionAsync()
    {
        DrainConfirmedPastes();
        if(checkingClipboard||deliveryBusy||inserting.Count>0)return;
        var candidates=destinations.Where(entry=>entry.Value is WindowsTextDelivery target&&target.HasPendingAcknowledgement).ToArray();
        if(candidates.Length==0)return;
        checkingClipboard=true;Changed?.Invoke();
        try
        {
            foreach(var entry in candidates)
            {
                var target=(WindowsTextDelivery)entry.Value;
                var confirmation=await automation.Run(token=>target.TryConfirmPendingPaste(token));
                if(confirmation is null)continue;
                lastDeliveryTarget=target;
                DrainConfirmedPastes();
                break;
            }
        }
        catch(Exception error) when(error is InvalidOperationException or OperationCanceledException)
        {Diagnostics.Error("pending insertion readback",error);}
        finally
        {
            DrainConfirmedPastes();
            checkingClipboard=false;
            Changed?.Invoke();banner.Refresh();
        }
    }

    public async Task RetryClipboardRestorationAsync()
    {
        if(restoringClipboard||!ClipboardRecovery.HasPending)return;
        restoringClipboard=true;
        try
        {
            // A stalled UIA worker must not hold the user's clipboard hostage.
            await Task.Run(ClipboardRecovery.TryRestorePending);
        }
        catch(Exception error)
        {
            Diagnostics.Error("automatic clipboard restoration",error);
        }
        finally
        {
            restoringClipboard=false;
            if(!ClipboardRecovery.HasPending)ClearClipboardRecoveryStatus();
            Changed?.Invoke();banner.Refresh();
        }
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
            if(active!=run)return;
            run.CompletionGate.Interrupt();
            if(!run.CanDeliver)return;
            run.CanDeliver=false;run.NeedsReview=true;run.Review.Reason=change.Reason;
            SaveReview(run.Review);Changed?.Invoke();banner.Refresh();
        });
    }

    async Task Deliver(Run run,bool wait=false)
    {
        if(!run.Recording||run.CompletionGate.IsFinishing||run.FinalOnlyDestination||!run.Settings.CanInsertLive||run.Delivery==null||run.PrimaryFinal&&run.Settings.VerifyAfterRecording)return;
        if(wait)await deliveryGate.WaitAsync();
        else if(!await deliveryGate.WaitAsync(0))return;
        deliveryBusy=true;
        bool CanBeginLive()=>ReferenceEquals(Volatile.Read(ref active),run)&&
            Volatile.Read(ref run.Recording)&&run.CompletionGate.CanBeginLiveDelivery;
        try
        {
            // Finish revokes work waiting on either the delivery gate or UIA worker.
            if(!CanBeginLive())return;
            var text=run.Text;
            lastDeliveryTarget=run.Delivery;
            var inserted=await automation.Run(token=>CanBeginLive()&&run.Delivery.Apply(text,token));
            if(!inserted)RememberLiveCompatibilityFailure(run);
            if(!CanBeginLive())return;
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
        catch(Exception error)
        {
            Diagnostics.Error("live cursor check",error);
            run.CanDeliver=false;run.NeedsReview=true;run.Review.Reason="Cursor checking is unavailable. Your transcript is preserved in Review.";
        }
        finally{deliveryBusy=false;deliveryGate.Release();Changed?.Invoke();}
    }

    async Task WithdrawForReview(Run run,bool restoreFocus)
    {
        if(run.Settings.AfterRecording!=AfterRecordingAction.InsertDirectly||run.Delivery==null||run.Delivery.IsConflict||run.Delivery.InsertedText.Length==0)return;
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
                run.Review.Reason="The first transcript is kept in the banner while verification runs. Matching text returns to the original cursor; differences open Review.";
                SaveReview(run.Review);
            }
        }
        catch(Exception){run.Review.Reason="The original draft could not be moved to Review. Accept will validate it before replacement.";}
        finally{deliveryBusy=false;deliveryGate.Release();}
    }

    public async Task<ReviewInsertionResult> InsertReviewAtSelectedCursor(ReviewDocument document)
    {
        DrainConfirmedPastes();document=CanonicalReview(document);
        if(document.InsertedAt!=null)return new(true,"This Review was already inserted.");
        if(ClipboardRecovery.HasPending||ClipboardRecovery.HasUnsettledTransaction)return new(false,"Windows is still returning the previous clipboard. Restoration retries automatically; try Insert again shortly.");
        if(!document.CanInsert)return new(false,"Resolve the remaining choices before inserting.");
        if(exiting||IsBusy||deliveryBusy||inserting.Count>0||!inserting.Add(document.Id))return new(false,"Finish the current operation, then try Insert again.");
        ITextDelivery? selected=null;var published=false;
        try
        {
            Changed?.Invoke();SaveReview(document);
            if(pending.UnsavedIds.Contains(document.Id))return new(false,"Protected saving failed. Your text is preserved; fix storage before inserting.");
            destinations.TryGetValue(document.Id,out var previous);
            // A new user-requested Insert is a fresh attempt, even when an older
            // attempt was unconfirmed. No automatic transport replay is enabled.
            var stamp=input.Epoch.Capture((long)insertionForegroundWindow());
            selected=await automation.Run(captureInsertionTarget);
            if(!input.Epoch.CanAdopt(stamp,(long)insertionForegroundWindow()))
                return new(false,"The selected cursor changed. Select the destination field and try Insert again.");
            // Exact same-field/whole-text/owned-end proof permits replacement of a
            // live draft. Otherwise this explicit action inserts at the new caret;
            // earlier live text elsewhere is left untouched.
            var adopted=selected is WindowsTextDelivery fresh&&previous is WindowsTextDelivery old&&fresh.TryAdoptOwnedRangeFrom(old);
            previous?.Invalidate();DrainConfirmedPastes();
            if(document.InsertedAt!=null)return new(true,"The earlier insertion was confirmed. Nothing was inserted again.");
            if(ClipboardRecovery.HasPending||ClipboardRecovery.HasUnsettledTransaction)return new(false,"Windows is still returning the previous clipboard. Restoration retries automatically; try Insert again shortly.");
            destinations[document.Id]=selected;published=true;
            var result=await AcceptReviewCore(document,false,stamp,true);
            if(result.Success&&previous?.InsertedText.Length>0&&!adopted)
                lastDeliveryMessage=DetailLabel="Inserted at the selected cursor. Earlier live text at another position was left untouched.";
            return result;
        }
        catch(InvalidOperationException)
        {return new(false,"Click the editable destination field first, then click Insert. Your text is preserved.");}
        catch(System.ComponentModel.Win32Exception error)
        {
            Diagnostics.Error("selected cursor insertion",error);
            return new(false,$"The selected field could not be validated (Windows error {error.NativeErrorCode}). Your text is preserved. Select the destination cursor and try Insert again.");
        }
        catch(Exception error)
        {Diagnostics.Error("selected cursor insertion",error);return new(false,"The selected field could not be validated. Your text is preserved; select a cursor and try Insert again.");}
        finally
        {
            if(!published)selected?.Invalidate();
            inserting.Remove(document.Id);Changed?.Invoke();
        }
    }

    Task<ReviewInsertionResult> AcceptReview(ReviewDocument document,bool restoreFocus=true,DestinationStamp? automaticStamp=null)=>
        AcceptReviewCore(document,restoreFocus,automaticStamp,false);
    async Task<ReviewInsertionResult> AcceptReviewCore(ReviewDocument document,bool restoreFocus,DestinationStamp? automaticStamp,bool reservationHeld)
    {
        DrainConfirmedPastes();
        document=CanonicalReview(document);
        if(document.InsertedAt!=null)return new(true,"This Review was already inserted.");
        if(ClipboardRecovery.HasPending)return new(false,ClipboardRecovery.Message);
        if(!document.CanInsert)return new(false,"Resolve the remaining choices before inserting.");
        if(exiting||IsBusy||deliveryBusy||(!reservationHeld&&!inserting.Add(document.Id)))return new(false,"Finish the current operation, then try Accept again.");
        try
        {
            Changed?.Invoke();
            if(!destinations.TryGetValue(document.Id,out var target))
                return new(false,"This Review has no active destination. Use Choose destination, click the field, then press "+Settings.Shortcut+".");
            SaveReview(document);
            if(pending.UnsavedIds.Contains(document.Id))return new(false,"Protected saving failed. Your choices are preserved in this window; fix storage before inserting.");
            bool inserted;
            lastDeliveryTarget=target;
            try{inserted=await automation.Run(token=>
            {
                if(automaticStamp is {} stamp&&!input.Epoch.CanAdopt(stamp,(long)insertionForegroundWindow()))return false;
                return target.Apply(document.OutputText,token,restoreFocus?DeliveryIntent.ExplicitFinal:DeliveryIntent.AutomaticFinal);
            },TimeSpan.FromSeconds(5));}
            catch(Exception){inserted=false;}
            DrainConfirmedPastes();
            if(document.InsertedAt!=null)return new(true,"The original insertion was confirmed. Nothing was sent.");
            if(!inserted)return new(false,target.Reason.Length>0?target.Reason:"The original destination could not be validated. Choose another destination.");
            RecordSuccessfulInsertion(document,target.Reason);
            return new(true,target.Reason.Length>0?target.Reason:"Reviewed text inserted.");
        }
        finally{if(!reservationHeld)inserting.Remove(document.Id);Changed?.Invoke();}
    }

    void RecordSuccessfulInsertion(ReviewDocument document,string deliveryWarning)
    {
        // Persist the receipt before cleanup. A cleanup failure must never trigger another insertion.
        document.InsertedAt=DateTimeOffset.UtcNow;document.DraftWithdrawn=false;
        lastTranscript=document.OutputText;lastDeliveryMessage=deliveryWarning.Length>0?deliveryWarning:"Finished text was inserted at the original cursor.";
        SaveReview(document);
        if(BannerReview?.Id==document.Id)bannerProgress=null;
        if(pending.UnsavedIds.Contains(document.Id))
            ConnectionMessage="Text was inserted, but its receipt could not be saved. Saved text is preserved; do not insert it again after restarting.";
        else
        {
            try{DismissReview(document.Id);}
            catch(Exception){ConnectionMessage="Text was inserted. Pending cleanup failed; this Review is marked as already inserted.";}
        }
        placementReview=null;StateLabel="Inserted";DetailLabel=deliveryWarning.Length>0?deliveryWarning:"Reviewed text inserted. Nothing was submitted.";
        compactReviewWindow?.Close();
        hideAt=DateTimeOffset.MinValue;Changed?.Invoke();
    }

    void ShowBannerReview(ReviewDocument document,bool openEditor=false)
    {
        if(ClipboardRecovery.HasPending)document.Reason=ClipboardRecovery.Message;
        placementReview=null;
        if(reviewWindow?.IsApplying==true||IsApplyingReview)return;
        if(reviewWindow is {IsTranscribing:true} details&&
            (details.ReviewId==document.Id||details.ReviewId==document.RetranscriptionOf))
        {
            details.Rebind(document);details.SetTranscriptionBusy(false);ConfigureReviewWindow(details);
            bannerProgress=null;hideAt=DateTimeOffset.MinValue;
            StateLabel=document.UnresolvedCount>0?"Review":"Ready to insert";DetailLabel=document.Reason;
            Changed?.Invoke();banner.Refresh();return;
        }
        reviewWindow?.Close();
        bannerProgress=new CompactReviewProgress(document);
        StateLabel=document.UnresolvedCount>0?"Review":"Ready to insert";
        DetailLabel=document.UnresolvedCount>0?"Choose the wording in Review.":"Your text is saved. Use Insert when you are ready.";
        hideAt=DateTimeOffset.MinValue;
        Changed?.Invoke();banner.Refresh();
        if(openEditor)
        {
            if(compactReviewWindow==null)
            {
                var window=new CompactReviewWindow(this);
                compactReviewWindow=window;
                window.Closed+=(_,_)=>{if(compactReviewWindow==window)compactReviewWindow=null;};
            }
            compactReviewWindow.Show();compactReviewWindow.Activate();
        }
    }

    public void SelectBannerPiece(string id)
    {
        if(IsBusy||IsApplyingReview||bannerProgress==null)return;
        bannerProgress.Select(id);Changed?.Invoke();
    }

    public void ChooseBannerPiece(string kind)
    {
        if(IsBusy||IsApplyingReview||bannerProgress==null||BannerReview?.InsertedAt!=null)return;
        bannerProgress.Choose(kind);SaveReview(bannerProgress.Document);Changed?.Invoke();
        if(bannerProgress.IsComplete&&!bannerProgress.Document.RequiresExplicitInsert)_ = ApplyBannerReview();
    }

    public void EditBannerPiece(string text)
    {
        if(IsBusy||IsApplyingReview||bannerProgress==null||BannerReview?.InsertedAt!=null)return;
        bannerProgress.Choose("custom",text);SaveReview(bannerProgress.Document);Changed?.Invoke();
        if(bannerProgress.IsComplete&&!bannerProgress.Document.RequiresExplicitInsert)_ = ApplyBannerReview();
    }

    public void KeepBannerWording()
    {
        if(IsBusy||IsApplyingReview||bannerProgress==null||BannerReview?.InsertedAt!=null)return;
        bannerProgress.KeepRemaining();SaveReview(bannerProgress.Document);Changed?.Invoke();
        if(!bannerProgress.Document.RequiresExplicitInsert)_ = ApplyBannerReview();
    }

    public bool SaveBannerEditedText()
    {
        if(IsBusy||IsApplyingReview||bannerProgress==null||
            BannerReview is not {} document||document.InsertedAt!=null||document.ManualFinalDraft is null)return false;
        bannerProgress.KeepRemaining();
        document.Reason="Text saved. Select your destination cursor and click Insert when ready.";
        SaveReview(document);Changed?.Invoke();
        return !IsReviewUnsaved(document.Id);
    }

    public bool UseWholeBannerTranscript(ReviewCopySource source)
    {
        if(IsBusy||IsApplyingReview||BannerReview is not {} document||document.InsertedAt!=null)return false;
        document.UseWholeTranscript(source);
        bannerProgress=new CompactReviewProgress(document);
        document.Reason="Selected transcript saved. You can edit it or Insert at your chosen cursor.";
        SaveReview(document);Changed?.Invoke();
        return !IsReviewUnsaved(document.Id);
    }

    public Task ApplyBannerReview()=>ApplyBannerReviewCore(false);
    public Task ApplyBannerReviewAtSelectedCursor()=>ApplyBannerReviewCore(true);
    async Task ApplyBannerReviewCore(bool selectedCursor)
    {
        if(IsBusy||IsApplyingReview||BannerReview is not {} document||!document.CanInsert)return;
        lastInsertionFailureId=document.Id;lastInsertionFailure=null;
        applyingBannerId=document.Id;
        try
        {
            Changed?.Invoke();
            var result=selectedCursor?await InsertReviewAtSelectedCursor(document):await AcceptReview(document);
            if(!result.Success&&BannerReview?.Id==document.Id)
            {lastInsertionFailure=result.Message;document.Reason=result.Message;StateLabel="Review";SaveReview(document);}
        }
        finally
        {
            if(applyingBannerId==document.Id)applyingBannerId=null;
            Changed?.Invoke();banner.Refresh();
        }
    }

    public void ChooseBannerDestination()
    {if(!IsBusy&&BannerReview is {} document&&!IsApplyingReview)ChooseReviewDestination(document);}

    public void OpenBannerDetails()
    {
        if(IsBusy||BannerReview is not {} document||IsApplyingReview)return;
        compactReviewWindow?.Close();
        bannerProgress=null;Changed?.Invoke();banner.Refresh();
        OpenReviewDetails(document);
    }

    void ChooseReviewDestination(ReviewDocument document)
    {
        DrainConfirmedPastes();
        if(ClipboardRecovery.HasPending){ShowClipboardRecovery();_=RetryClipboardRestorationAsync();return;}
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
        var result=await InsertReviewAtSelectedCursor(document);
        if(result.Success){reviewWindow?.Close();compactReviewWindow?.Close();placementReview=null;}
        else{document.Reason=result.Message;SaveReview(document);OpenReview(document);}
    }

    public void RecheckShortcut()
    {
        if(input.IsHoldingShortcut){ShortcutMessage="Shortcut changes apply after you release the current shortcut.";Changed?.Invoke();return;}
        input.Shortcut=ShortcutSpec.Parse(Settings.Shortcut);input.Mode=Settings.ShortcutMode;
        var collision=GlobalInput.TypeWhisperRunning()&&Settings.Shortcut=="Ctrl+Shift";
        input.ShortcutEnabled=Settings.EnableShortcut&&!noHotkey&&!collision;
        ShortcutMessage=noHotkey?"Shortcut disabled for this test launch (--no-hotkey).":
            collision?"TypeWhisper is running. Quit it, then Recheck shortcut before using Ctrl+Shift here.":
            !Settings.EnableShortcut?"Shortcut disabled in Settings.":Settings.ShortcutMode==ShortcutMode.Hold?"Hold "+Settings.Shortcut+" to record; release to finish.":Settings.Shortcut+" starts and stops dictation.";
        Changed?.Invoke();
    }

    public void SaveSettings(Settings settings)
    {
        if(Models.FirstOrDefault(m=>m.Id==settings.PrimaryModel) is {} model)
        {
            LanguagePolicy.ValidateForModel(model,settings.Language,settings.PreferredLanguages);
            if(settings.VerifyAfterRecording&&!Models.Any(m=>m.Id==settings.VerificationModel&&m.Id!=model.Id&&ModelEligibility.CanVerify(m,settings.Language,model,settings.PreferredLanguages)))
                throw new ArgumentException("Choose a verification model supporting every preferred Auto language supported by Dictation.");
        }
        settings.Save();
        Settings=settings;
        ThemeManager.Apply(settings.Theme);
        RecheckShortcut();
    }

    public bool ResetDeliveryCompatibility()
    {
        var previous=Settings.FinalOnlyApplications;
        Settings.FinalOnlyApplications=[];
        try {Settings.Save();}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Settings.FinalOnlyApplications=previous;
            ConnectionMessage="Could not save the delivery preference. Your previous choices were kept.";
            Changed?.Invoke();return false;
        }
        Changed?.Invoke();return true;
    }

    void RememberLiveCompatibilityFailure(Run run)
    {
        if(run.FinalOnlyDestination||!run.Settings.CanInsertLive||run.Delivery?.HasTransportFailure!=true)return;
        if(!DestinationDeliveryPolicy.RememberLiveFailure(Settings.FinalOnlyApplications,run.DestinationProcessName,true))return;
        try {Settings.Save();}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {ConnectionMessage="The safer delivery mode could not be saved. Your transcript is preserved.";}
    }

    void BeginFinish(Run run)
    {
        if(run.CompletionGate.IsFinishing)return;
        run.CompletionGate.BeginFinish();
        if(run.Settings.AfterRecording!=AfterRecordingAction.InsertDirectly)return;
        var stamp=input.Epoch.Capture((long)WindowsTextDelivery.ForegroundWindow);
        run.FinishStamp=stamp;
        run.FinishDestinationCheck=CheckFinishDestination(run,stamp);
    }

    async Task CheckFinishDestination(Run run,DestinationStamp stamp)
    {
        var current=false;
        try
        {
            if(run.Delivery is {} target&&stamp.Window==(long)target.Window)
                current=await automation.Run(token=>target.IsOriginalFieldCurrent(token));
            current=current&&input.Epoch.CanAdopt(stamp,(long)WindowsTextDelivery.ForegroundWindow);
        }
        catch(Exception error) when(error is InvalidOperationException or OperationCanceledException)
        {current=false;}
        run.CompletionGate.ConfirmOriginalField(current);
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
    readonly LocalServiceStartup apiStartup=new();
    public Task InitializeConnection()=>StartLocalApi();
    public async Task StartLocalApi()
    {
        try
        {
            var endpoint=Settings.ServerUri();
            var result=await apiStartup.EnsureStarted(endpoint,()=>LocalApiLauncher.IsListening(endpoint),async()=>
            {
                ConnectionMessage="Starting the local transcription service…";Changed?.Invoke();
                await LocalApiLauncher.Start(endpoint.Port);
            });
            if(result==LocalServiceStartResult.UnsupportedEndpoint)
            {ConnectionMessage="Use the Local Dictation session service address. Port 8123 belongs to the existing batch service.";Changed?.Invoke();return;}
            await RefreshConnection();
        }
        catch(Exception error)
        {
            Diagnostics.Error("local service startup",error);
            ConnectionMessage="Could not start the local service. Check its address and launcher, then try Start local API again.";
            Changed?.Invoke();
        }
    }

    public bool SetShortcutCapture(bool capturing)
    {
        if(capturing&&(IsBusy||IsApplyingReview))return false;
        input.CapturingShortcut=capturing;return true;
    }
    void StartHeldShortcut()
    {
        if(IsBusy)return;
        Toggle();heldShortcutRun=active;
    }
    void StopHeldShortcut()
    {
        var owned=heldShortcutRun;heldShortcutRun=null;
        if(owned!=null&&ReferenceEquals(active,owned)&&owned.Recording)Stop();
        RecheckShortcut();
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
        if(ClipboardRecovery.HasPending){ShowClipboardRecovery();_=RetryClipboardRestorationAsync();return;}
        if(exiting||reviewWindow?.IsApplying==true||IsApplyingReview||inserting.Count>0||deliveryBusy||active?.Closing==true)return;
        placementReview=null;
        if(active!=null&&!active.Verifying)return;
        if(GlobalInput.TypeWhisperRunning()&&Settings.Shortcut=="Ctrl+Shift"&&input.ShortcutEnabled){RecheckShortcut();return;}
        if(!Connected){_ = RefreshConnection();ShowSettings();return;}
        var snapshot=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Settings))!;
        if(reviewOnly)snapshot.InsertAtCursor=false;
        var model=Models.FirstOrDefault(m=>m.Id==snapshot.PrimaryModel&&m.Installed);
        if(model==null){ShowSettings();return;}
        try{LanguagePolicy.ValidateForModel(model,snapshot.Language,snapshot.PreferredLanguages);}
        catch(ArgumentException error){ConnectionMessage=error.Message;ShowSettings();Changed?.Invoke();return;}
        if(snapshot.VerifyAfterRecording&&!Models.Any(m=>m.Id==snapshot.VerificationModel&&ModelEligibility.CanVerify(m,snapshot.Language,model,snapshot.PreferredLanguages)&&m.Id!=model.Id))
        {ConnectionMessage="Choose an installed, runtime-tested verification model distinct from Dictation.";ShowSettings();Changed?.Invoke();return;}
        languageWindow?.Close();
        if(active?.Verifying==true)active.Cancel.Cancel();
        var destination=input.Epoch.Capture((long)WindowsTextDelivery.ForegroundWindow);
        bannerAnchor=(nint)destination.Window;
        var run=new Run(snapshot){Destination=destination};
        active=run;input.SessionActive=true;
        StateLabel="Preparing";DetailLabel="Opening the selected microphone…";hideAt=DateTimeOffset.MinValue;
        TrackRecording(Record(run));
    }

    async Task ObserveRecording(Task recording)
    {
        try{await recording;}
        catch(Exception ex)
        {
            Diagnostics.Error("recording cleanup",ex);
            ConnectionMessage="Recording cleanup did not finish. Check History before exiting.";
        }
        finally{recordingTasks.Remove(recording);}
    }

    void ConfigureDestination(Run run,ITextDelivery delivery,string? processName)
    {
        run.Delivery=delivery;destinations[run.Review.Id]=delivery;
        run.DestinationProcessName=processName;
        run.FinalOnlyDestination=DestinationDeliveryPolicy.InsertsAfterFinish(processName,run.Settings.FinalOnlyApplications);
        run.CanDeliver=run.Settings.CanInsertLive&&!run.FinalOnlyDestination;
        if(!run.CanDeliver)
        {
            run.NeedsReview=true;
            run.Review.Reason=run.Settings.AfterRecording==AfterRecordingAction.PreviewFirst
                ?"Preview first: the destination stays unchanged. Review the text, then use Insert."
                :run.FinalOnlyDestination
                ?"The completed text will be inserted at the original cursor after recording."
                :"Preview only while recording. The completed text will return to the original cursor.";
        }
    }

    async Task Record(Run run,byte[]? replay=null)
    {
        Task? sender=null,receiver=null;
        try
        {
            run.Capture.Faulted+=message=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
            {if(active!=run)return;ConnectionMessage=message;run.Failure=message;run.NeedsReview=true;run.Cancel.Cancel();if(run.DestinationChecked)Changed?.Invoke();});
            run.Capture.LimitReached+=()=>System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>
            {if(active!=run)return;BeginFinish(run);run.Recording=false;StateLabel="Finishing · recording limit";if(run.DestinationChecked)Changed?.Invoke();});
            if(replay==null)run.Capture.Start(run.Settings.MicrophoneId);
            run.Started=DateTimeOffset.Now;
            run.Stage="destination";
            // A replay retains its original guarded destination; it never captures a new field.
            if(replay==null)
            {
                try
                {
                    if(!input.FocusTrackingAvailable)throw new InvalidOperationException("Focus tracking unavailable.");
                    var delivery=await automation.Run(token=>WindowsTextDelivery.Capture(token));
                    if(!input.Epoch.CanAdopt(run.Destination,(long)WindowsTextDelivery.ForegroundWindow))
                    {delivery.Invalidate();throw new InvalidOperationException("The initial destination changed.");}
                    if(active!=run||run.Cancel.IsCancellationRequested)
                    {delivery.Invalidate();run.Cancel.Token.ThrowIfCancellationRequested();return;}
                    ConfigureDestination(run,delivery,WindowsDestinationApplication.ProcessName(delivery.Window));
                }
                catch(Exception ex){run.Review.Reason=ex is InvalidOperationException?ex.Message:
                    "The original cursor could not be validated. Dictation continues in this preview.";run.NeedsReview=true;}
            }
            run.Cancel.Token.ThrowIfCancellationRequested();
            if(active!=run)return;
            run.DestinationChecked=true;
            PrepareRecordingReview(run,replay!=null);
            lastTranscript=lastDeliveryMessage="";
            Changed?.Invoke();banner.Refresh();
            run.Stage="connection";
            if(replay!=null)
            {
                // transcript.final precedes server cleanup. Do not replay audio until
                // the previous GPU owner has actually released admission.
                using var api=new ApiClient(run.Settings);
                await api.WaitForIdle(run.Cancel.Token);
            }
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(run.Cancel.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                await run.Stream.Connect(run.Settings,timeout.Token);
            }
            if(active==run)StateLabel=replay==null?"Listening":"Retranscribing";
            run.Stage="audio or transcription";
            receiver=run.Stream.Receive(e=>Handle(run,e),run.Cancel.Token);
            sender=replay==null?SendAudio(run):SendReplayAudio(run,replay);
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
            var languageRetry=run.RetrySettings!=null&&run.PrimaryFinal&&ex is OperationCanceledException;
            run.AutoInsertBlocked|=!languageRetry;
            var cancelled=ex is OperationCanceledException&&run.Cancel.IsCancellationRequested&&run.Failure.Length==0;
            if(!cancelled)Diagnostics.Error(run.Stage,ex);
            run.NeedsReview=true;
            run.Review.Reason=ex is OperationCanceledException
                ?"Recording or verification stopped. Received text is preserved; some text may already be inserted."
                :"Transcription was interrupted. Received text is preserved; check your destination before pasting.";
            if(active==run&&!languageRetry)
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
            var audio=replay??run.Capture.TakeAudio();
            try
            {
                if(audio.Length>0)
                {
                    historyStore.Add(run.Review,audio);
                    History=historyStore.Load();
                }
            }
            catch(Exception)
            {
                run.NeedsReview=true;
                run.Review.Reason="History could not save this recording. The text is kept in History; audio recovery is unavailable.";
                ConnectionMessage="History could not be saved. Check History before exiting.";
            }
            try
            {
                if(run.LanguageSelection is {} selection)await selection.Task;
                if(active==run&&run.RetrySettings!=null&&audio.Length>0&&!exiting)
                    await ReplayAtOriginalDestination(run,audio);
                else await CompleteRun(run);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(audio);
                run.Cancel.Dispose();
            }
        }
    }

    async Task CompleteRun(Run run)
    {
        if(run.Text.Length>0)SaveReview(run.Review);
        if(active!=run)return;
        BeginFinish(run);
        await run.FinishDestinationCheck;
        run.AutoInsertBlocked|=!run.CompletionGate.CanInsertAutomatically;
        if(run.AutoInsertBlocked&&run.NormalCompletion&&run.Settings.AfterRecording==AfterRecordingAction.InsertDirectly)
            run.Review.Reason="Your text is ready. Use Insert when you return to the original field.";
        if(run.Settings.VerifyAfterRecording&&run.Text.Length>0&&!run.Review.DraftWithdrawn&&!run.AutoInsertBlocked)
            await WithdrawForReview(run,false);
        if(active!=run)return;
        run.AutoInsertBlocked|=!run.CompletionGate.CanInsertAutomatically||!input.Epoch.CanAdopt(run.FinishStamp,(long)WindowsTextDelivery.ForegroundWindow);
        lastTranscript=run.Text;lastDeliveryMessage=run.Review.Reason;
        active=null;input.SessionActive=false;hideAt=DateTimeOffset.Now.AddSeconds(6);
        if(run.Text.Length==0)
        {
            if(StateLabel!="Unavailable")StateLabel="Stopped";
            if(!RestoreEmptySavedReview(run))DetailLabel="No transcript was returned. Check the microphone level and try again.";
            Changed?.Invoke();banner.Refresh();return;
        }
        var action=CompactCompletion.Decide(run.NormalCompletion&&
            (run.Settings.AfterRecording==AfterRecordingAction.PreviewFirst||!run.AutoInsertBlocked),run.PrimaryFinal,
            run.Settings.VerifyAfterRecording,run.Review.HasCompletedVerification,run.Review.UnresolvedCount,run.Settings.AfterRecording);
        if(action==CompactCompletionAction.Insert)
        {
            if(destinations.ContainsKey(run.Review.Id))
            {
                // Ordinary completion and verification agreement return to the
                // captured field before any interactive Review surface appears.
                var result=await AcceptReview(run.Review,false,run.FinishStamp);
                if(result.Success){banner.Refresh();return;}
                run.Review.Reason=result.Message;
            }
            else run.Review.Reason="The text is ready. Choose destination to place it in your selected field.";
        }
        ShowBannerReview(run.Review,action is CompactCompletionAction.ReviewDifferences or CompactCompletionAction.Preview);
    }

    async Task SendReplayAudio(Run run,byte[] pcm)
    {
        for(var offset=0;offset<pcm.Length;offset+=64000)
        {
            run.Cancel.Token.ThrowIfCancellationRequested();
            var frame=pcm.AsSpan(offset,Math.Min(64000,pcm.Length-offset)).ToArray();
            try{await run.Stream.SendAudio(frame,run.Cancel.Token);}
            finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(frame);}
        }
        await run.Stream.Finish(run.Cancel.Token);
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
            BeginFinish(run);run.Recording=false;run.Capture.Stop();
            if(active==run)StateLabel="Finishing · recording limit";
        }
        else if(type is "transcript.updated" or "transcript.final")
        {
            run.Text=e.GetProperty("text").GetString()??"";
            if(e.TryGetProperty("language",out var detected)&&detected.ValueKind==JsonValueKind.String)run.Review.Language=detected.GetString();
            if(e.TryGetProperty("language_mode",out var mode)&&mode.ValueKind==JsonValueKind.String)run.Review.LanguageMode=mode.GetString();
            run.Review.SetPrimary(run.Text);
            SaveReview(run.Review);
            if(type=="transcript.final")
            {
                BeginFinish(run);
                run.PrimaryFinal=true;run.Recording=false;run.Capture.Stop();
                if(run.Settings.VerifyAfterRecording&&run.RetrySettings==null)
                {run.NeedsReview=true;await WithdrawForReview(run,false);}
                if(run.RetrySettings!=null)run.Cancel.Cancel();
            }
            else if(run.Delivery!=null)await Deliver(run,true);
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
            run.Review.Reason=run.Review.RequiresExplicitInsert
                ?"Choose the correct wording. The next phrase opens automatically. Use Insert when you are ready."
                :"Choose the correct wording. The next phrase opens automatically; the last choice inserts the finished text.";
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
        BeginFinish(active);
        active.Recording=false;StateLabel="Finishing";
        active.Capture.Stop();if(active.DestinationChecked)Changed?.Invoke();
    }
    public void Cancel()
    {
        languageWindow?.Close();
        if(IsApplyingReview||inserting.Count>0)return;
        // Escape relinquishes the hold without a later Stop command; apply its deferred settings now.
        if(heldShortcutRun!=null&&!input.IsHoldingShortcut)
        {heldShortcutRun=null;RecheckShortcut();}
        if(active==null)
        {
            // Hide the banner; protected text remains available in History.
            bannerProgress=null;placementReview=null;hideAt=DateTimeOffset.MinValue;Changed?.Invoke();banner.Refresh();return;
        }
        active.RetrySettings=null;
        active.AutoInsertBlocked=true;active.NeedsReview=true;active.Delivery?.Invalidate();active.Cancel.Cancel();active.Capture.Stop();
    }
    public void EmergencyReview()
    {
        if(active!=null)
        {
            active.AutoInsertBlocked=true;
            active.NeedsReview=true;active.Delivery?.Invalidate();
            if(active.Text.Length>0)SaveReview(active.Review);
            active.RetrySettings=null;
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
            catch(Exception){ConnectionMessage="History changes could not be saved. Your saved Review text is preserved.";}
        }
        if(!pending.Save(document))
            ConnectionMessage="Protected saving failed. Review is in memory only: copy it before exiting.";
    }
    public void DismissReview(string id)
    {
        // Revoke further acknowledgement before dropping the carrier; consume any
        // proof already published so History cannot lose an insertion receipt.
        if(destinations.TryGetValue(id,out var existing))existing.Invalidate();
        DrainConfirmedPastes();
        pending.Dismiss(id);
        if(BannerReview?.Id==id)bannerProgress=null;
        if(destinations.Remove(id,out var target))target.Invalidate();
        Changed?.Invoke();
    }
    public void OpenReview(ReviewDocument document,bool activate=true)
    {
        if(IsBusy){ConnectionMessage="Finish recording before opening another Review.";Changed?.Invoke();return;}
        ShowBannerReview(CanonicalReview(document),true);
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
        var window=new ReviewWindow(document,SaveReview,DismissReview,doc=>InsertReviewAtSelectedCursor(doc),ChooseReviewDestination){ShowActivated=activate};
        ConfigureReviewWindow(window);
        reviewWindow=window;window.Closed+=(_,_)=>{if(reviewWindow==window)reviewWindow=null;};
        window.Show();if(activate)window.Activate();
    }
    void ConfigureReviewWindow(ReviewWindow window)
    {
        window.SetSaveStatus(()=>!IsReviewUnsaved(window.ReviewId));
        window.SetVerificationAction(()=>VerificationChoices(window.Document),model=>StartSavedVerification(window.Document,model),CanVerifyReview(window.Document));
    }
    void BindTranscribingReview(ReviewDocument source,ReviewDocument next)
    {
        if(reviewWindow is {} window&&window.ReviewId==source.Id)
        {window.Rebind(next);ConfigureReviewWindow(window);window.SetTranscriptionBusy(true);}
        else if(compactReviewWindow!=null&&source.RequiresExplicitInsert&&BannerReview?.Id==source.Id)
            bannerProgress=new CompactReviewProgress(next);
    }
    void PrepareRecordingReview(Run run,bool replay)
    {
        var savedReplay=replay&&run.ReviewFallback!=null&&run.Review.RequiresExplicitInsert;
        if(savedReplay&&reviewWindow is {IsTranscribing:true} details&&details.ReviewId==run.Review.Id)
        {bannerProgress=null;return;}
        if(savedReplay&&compactReviewWindow!=null&&BannerReview?.Id==run.Review.Id)return;
        bannerProgress=null;compactReviewWindow?.Close();reviewWindow?.Close();
    }
    bool RestoreEmptySavedReview(Run run)
    {
        if(run.ReviewFallback is not {} previous)return false;
        // Audio comparison owns a detached snapshot. Editing must resume on the
        // current document so saved corrections and late insertion receipts win.
        previous=CanonicalReview(previous);
        const string message="This retranscription returned no text. Your previous Review has been restored.";
        if(reviewWindow is {IsTranscribing:true} details&&details.ReviewId==run.Review.Id)
        {
            details.Rebind(previous);details.SetTranscriptionBusy(false);ConfigureReviewWindow(details);
            details.ShowStatus(message);bannerProgress=null;
        }
        else if(compactReviewWindow!=null&&BannerReview?.Id==run.Review.Id&&run.Review.RequiresExplicitInsert)
            bannerProgress=new CompactReviewProgress(previous);
        else return false;
        lastTranscript=previous.OutputText;DetailLabel=lastDeliveryMessage=message;hideAt=DateTimeOffset.MinValue;
        return true;
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
    public void DeleteDictation(string id)
    {
        if(IsBusy||IsApplyingReview||reviewWindow?.IsApplying==true||inserting.Count>0)throw new InvalidOperationException("Finish the current operation before deleting this dictation.");
        if(reviewWindow?.ReviewId==id)reviewWindow.Close();
        if(BannerReview?.Id==id)compactReviewWindow?.Close();
        if(placementReview?.Id==id)placementReview=null;
        historyStore.Delete(id);History=historyStore.Load();DismissReview(id);canonicalReviews.Remove(id);
    }
    public void ShowHistory()
    {
        ShowSettings();settingsWindow!.SelectHistory();
    }
    bool ClipboardBlocksExit()
    {
        // Only an active clipboard replacement/restoration holds Exit. Read-only
        // late insertion checks do not retain the clipboard or require recovery.
        var unsettled=ClipboardRecovery.HasUnsettledTransaction;
        DrainConfirmedPastes();
        if(!unsettled&&!restoringClipboard)return false;
        exiting=false;
        if(ClipboardRecovery.HasPending){ShowClipboardRecovery();_=RetryClipboardRestorationAsync();}
        else
        {
            const string message="Clipboard delivery is still running or waiting for the destination. Keep Local Dictation open and wait for it to finish before exiting.";
            ConnectionMessage=DetailLabel=lastDeliveryMessage=message;
            if(BannerReview is {} document){document.Reason=message;SaveReview(document);}
            hideAt=DateTimeOffset.Now.AddSeconds(6);Changed?.Invoke();banner.Refresh();
        }
        return true;
    }

    async Task<bool> PrepareForExit()
    {
        languageWindow?.Close();
        if(ClipboardBlocksExit())return false;
        exiting=true;
        // Serialize invalidation with any final readback publication, then drain
        // facts that won that race before saving and closing the process.
        foreach(var target in destinations.Values.Distinct())target.Invalidate();
        DrainConfirmedPastes();
        EmergencyReview();active?.Capture.Stop();
        // Include older runs cancelled when a new recording superseded verification.
        await Task.WhenAll(recordingTasks.ToArray());
        if(ClipboardBlocksExit())return false;
        return true;
    }

    async void Exit()
    {
        if(exiting)return;
        if(ClipboardBlocksExit())return;
        if(IsBusy&&MessageBox.Show("Stop this session and exit? Received text and its original recording will be saved before closing.",
            "Exit Local Dictation",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        // The confirmation pumps the dispatcher; insertion may have started while it was open.
        if(IsApplyingReview||reviewWindow?.IsApplying==true||inserting.Count>0)
        {ConnectionMessage="Wait for the insertion check before exiting.";Changed?.Invoke();return;}
        try{if(!await PrepareForExit())return;}
        catch(Exception)
        {exiting=false;ConnectionMessage="Recording cleanup did not finish. Check History before exiting.";Changed?.Invoke();return;}
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
        disposed=true;ClipboardRecovery.Changed-=ClipboardRecoveryChanged;
        timer.Stop();reconnectTimer.Stop();resumeTimer.Stop();input.Dispose();tray.Visible=false;tray.Dispose();active?.Cancel.Cancel();automation.Dispose();
    }
}
