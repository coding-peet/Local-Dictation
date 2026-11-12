using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

namespace LocalDictation.DeliveryProbe;

static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Contains("--diagnostic-checks"))
        {
            if(args.Length!=3||args[0]!="--diagnostic-checks"||args[1]!="--report")
            {Environment.ExitCode=2;return;}
            DiagnosticChecks.Run(Path.GetFullPath(args[2]));
            return;
        }
        if(args.Length==3&&args[0]=="--clipboard-checks"&&args[1]=="--report")
        {
            var path=Path.GetFullPath(args[2]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try{ClipboardChecks.Run();File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=true,NativeFormatCloningOnly=true,UserClipboardUnchanged=true}));}
            catch(Exception error){Environment.ExitCode=1;File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=false,NativeFormatCloningOnly=true,Error=error.ToString()}));}
            return;
        }
        var report=args.Length==2&&args[0]=="--report"?Path.GetFullPath(args[1]):
            Path.Combine(@"C:\AI\LocalDictation\diagnostics","delivery-probe-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");
        var application=new System.Windows.Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
        application.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/LocalDictation;component/Theme.xaml",UriKind.Relative)});
        application.Run(new ProbeWindow(report));
    }
}

sealed record ProbeInputTrace(DateTimeOffset TimeUtc,double ElapsedMilliseconds,string Check,string Event,
    int CaptureAttempt,bool CaptureInProgress,long CaptureWindow,int? CurrentCapture,long? CurrentWindow,
    string? Reason,bool? FocusOnly,long? RootWindow,bool? OperationActive,bool? OwnsProgrammaticFocus,bool Suspended);

sealed record ParagraphProjectionResult(string ProcessContext,int PasteCommands,int CompleteNativePastes,int CoalescedLineFeeds,
    string Before,string FullyPasted,string Observed,int FullPasteCaret,int Caret,bool? Applied,bool ClosedBeforeRecovery,DeliveryDiagnostic? FirstPasteDiagnostic);

sealed record StepResult(string Name,bool Passed,double Milliseconds,string Detail,string Target,string Other,bool? Conflict,DeliveryDiagnostic? Diagnostic,
    ParagraphProjectionResult? ParagraphProjection=null);

sealed class ProbeWindow : Window
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const string Prefix="Prefix | ",Suffix=" | Suffix",OtherText="Other field must stay untouched.";
    const string Draft="pôvodný diktát",Expanded="pôvodný diktát pokračuje",Approved="overený diktát";
    readonly TextBox target=new(){MinHeight=62,FontSize=16,VerticalContentAlignment=VerticalAlignment.Center};
    readonly TextBox other=new(){MinHeight=48,FontSize=14};
    readonly TextBlock status=new(){Text="Ready to run native delivery checks",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)};
    readonly TextBlock log=new(){FontFamily=new("Consolas"),TextWrapping=TextWrapping.Wrap,LineHeight=21};
    readonly Button run=new(){Content="Run checks",Padding=new(18,9,18,9),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,14,0,14)};
    readonly string reportPath;
    readonly List<StepResult> results=[];
    WindowsTextDelivery? current;
    GlobalInput? observer;
    Window? reviewSurface,temporaryTarget;
    int observedInputs,ignoredProgrammaticFocus;
    ParagraphProjectionResult? paragraphProjection;
    readonly List<ProbeInputTrace> inputTrace=[];
    readonly Dictionary<WindowsTextDelivery,int> captureIds=[];
    readonly Stopwatch inputTraceClock=new();
    string activeCheck="";
    int captureAttempt;
    bool captureInProgress;
    long captureWindow;

    // GlobalInput hooks and these capture/step continuations run on this UI
    // thread. This trace reads only numeric window handles and the delivery's atomic
    // operation flag; it does not query a provider or change delivery guards.
    void TraceInput(string kind,DestinationInput? input=null,bool? operationActive=null,bool? ownsFocus=null,bool suspended=false)
    {
        var owned=current;
        int? id=owned!=null&&captureIds.TryGetValue(owned,out var capturedId)?capturedId:null;
        inputTrace.Add(new(DateTimeOffset.UtcNow,inputTraceClock.Elapsed.TotalMilliseconds,activeCheck,kind,
            captureAttempt,captureInProgress,captureWindow,id,owned==null?null:(long)owned.Window,
            input?.Reason,input?.FocusOnly,input==null?null:(long)input.Value.RootWindow,
            operationActive??(owned==null?null:owned.OwnsProgrammaticFocus(owned.Window)),ownsFocus,suspended));
    }

    public ProbeWindow(string reportPath)
    {
        this.reportPath=reportPath;Title="Local Dictation delivery probe";
        Width=980;Height=790;MinWidth=760;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new DockPanel{Margin=new(24)};
        var header=new StackPanel();header.Children.Add(status);
        header.Children.Add(new TextBlock{Text="Synthetic local TextBoxes only. No microphone, model or external destination is used.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,18)});
        header.Children.Add(new TextBlock{Text="Owned dictation target (synthetic)",FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,6)});
        System.Windows.Automation.AutomationProperties.SetAutomationId(target,"DeliveryProbeTarget");
        header.Children.Add(target);
        header.Children.Add(new TextBlock{Text="Other field — must never receive dictation",FontWeight=FontWeights.SemiBold,Margin=new(0,14,0,6)});
        System.Windows.Automation.AutomationProperties.SetAutomationId(other,"DeliveryProbeOther");
        header.Children.Add(other);header.Children.Add(run);
        DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
        var path=new TextBox{Text=reportPath,IsReadOnly=true,BorderThickness=new(0),Background=Brushes.Transparent,TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,0)};
        DockPanel.SetDock(path,Dock.Bottom);panel.Children.Add(path);
        panel.Children.Add(new ScrollViewer{Content=log,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=panel;
        ResetText();run.Click+=async(_,_)=>await RunChecks();
        Closed+=(_,_)=>{observer?.Dispose();reviewSurface?.Close();temporaryTarget?.Close();};
    }

    void ResetText(){target.Text=Prefix+Suffix;other.Text=OtherText;target.Select(Prefix.Length,0);}
    static void Require(bool condition,string detail){if(!condition)throw new InvalidOperationException(detail);}
    static async Task Settle(){await Dispatcher.Yield(DispatcherPriority.ContextIdle);await Task.Delay(120);}
    async Task FocusTarget(int caret)
    {
        Activate();target.Focus();target.Select(caret,0);await Settle();
    }
    async Task FocusOther(){Activate();other.Focus();other.Select(0,0);await Settle();}
    async Task<WindowsTextDelivery> CaptureOwn(AutomationWorker worker,Window owner)
    {
        var expected=new WindowInteropHelper(owner).Handle;
        var attempt=++captureAttempt;captureWindow=(long)expected;captureInProgress=true;var succeeded=false;
        TraceInput("capture-start");
        try
        {
            var captured=await worker.Run(token=>WindowsTextDelivery.Capture(token,allowOwnProcess:true));
            if(captured.Window!=expected)
            {captured.Invalidate();throw new InvalidOperationException("Capture escaped the fixture window; no insertion was attempted.");}
            captureIds[captured]=attempt;succeeded=true;
            return captured;
        }
        finally
        {
            captureInProgress=false;
            // The caller publishes current after awaiting CaptureOwn. If an old
            // event arrives first, its trace retains the old CurrentCapture ID.
            TraceInput(succeeded?"capture-complete":"capture-failed");
        }
    }
    async Task ShowReviewSurface()
    {
        if(reviewSurface==null)
        {
            var button=new Button{Content="Synthetic Review surface — waiting for product focus restoration",Padding=new(16),Margin=new(20)};
            reviewSurface=new Window{Title="Delivery probe — synthetic Review",Width=650,Height=160,Owner=this,Content=button,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        }
        reviewSurface.Show();reviewSurface.Activate();((Button)reviewSurface.Content).Focus();await Settle();
    }
    async Task Step(string name,Func<Task> check,bool continueAfterFailure=false)
    {
        status.Text="Running: "+name;activeCheck=name;paragraphProjection=null;var watch=Stopwatch.StartNew();TraceInput("step-start");
        try
        {
            await check();results.Add(new(name,true,watch.Elapsed.TotalMilliseconds,"Passed",target.Text,other.Text,current?.IsConflict,current?.LastDiagnostic,paragraphProjection));
            log.Text+=$"PASS  {name} ({watch.Elapsed.TotalMilliseconds:F0} ms)\n";
        }
        catch(Exception exception)
        {
            results.Add(new(name,false,watch.Elapsed.TotalMilliseconds,exception.ToString(),target.Text,other.Text,current?.IsConflict,current?.LastDiagnostic,paragraphProjection));
            log.Text+=$"FAIL  {name}\n{exception}\n";if(!continueAfterFailure)throw;
        }
        finally{TraceInput("step-finished");}
    }

    static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,Members)!.GetValue(instance)!;
    static void Set(object instance,string name,object? value)=>instance.GetType().GetField(name,Members)!.SetValue(instance,value);
    async Task ControllerCompletion(AutomationWorker worker,bool verify)
    {
        var previousHome=Environment.GetEnvironmentVariable("LOCAL_DICTATION_HOME");
        var syntheticHome=Path.Combine(Path.GetTempPath(),"LocalDictation-NativeCompletion-"+Guid.NewGuid().ToString("N"));
        AppController? controller=null;object? runState=null;
        try
        {
            Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",syntheticHome);
            controller=new AppController(noHotkey:true);
            foreach(var name in new[]{"timer","reconnectTimer","resumeTimer"})Field<DispatcherTimer>(controller,name).Stop();
            ResetText();await FocusTarget(Prefix.Length);current=await CaptureOwn(worker,this);
            var settings=new Settings{InsertAtCursor=false,VerifyAfterRecording=verify,PrimaryModel="native-fixture/dictation",VerificationModel="native-fixture/verification"};
            var runType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
            runState=Activator.CreateInstance(runType,Members,null,[settings],null)!;
            var document=Field<ReviewDocument>(runState,"Review");
            Set(runState,"Delivery",current);Set(runState,"DestinationChecked",true);Set(runState,"Recording",false);Set(runState,"Closing",true);
            Set(controller,"active",runState);
            Field<Dictionary<string,ITextDelivery>>(controller,"destinations")[document.Id]=current;
            async Task Handle(object payload)
            {
                using var parsed=JsonDocument.Parse(JsonSerializer.Serialize(payload));
                await (Task)typeof(AppController).GetMethod("Handle",Members)!.Invoke(controller,[runState,parsed.RootElement.Clone()])!;
            }
            await Handle(new{type="transcript.final",text=Approved});
            Require(target.Text==Prefix+Suffix,"Preview-only final event inserted before completion.");
            if(verify)await Handle(new{type="verification.completed",verification=Approved,pieces=new[]{new ReviewPiece("common",Approved,Approved,false)}});
            await Handle(new{type="session.completed"});
            await (Task)typeof(AppController).GetMethod("CompleteRun",Members)!.Invoke(controller,[runState])!;
            Require(target.Text==Prefix+Approved+Suffix,"Controller did not complete native delivery: "+current.Reason+" / "+document.Reason);
            Require(document.InsertedAt!=null,"Native controller completion did not retain an insertion receipt.");
            Require(controller.BannerReview==null,"Successful native completion left a pending Review.");
            Require(other.Text==OtherText,"Native controller completion changed the unrelated field.");
            await (Task)typeof(AppController).GetMethod("CompleteRun",Members)!.Invoke(controller,[runState])!;
            Require(target.Text==Prefix+Approved+Suffix,"Repeated completion duplicated native text.");
        }
        finally
        {
            controller?.Dispose();
            if(controller!=null)Field<BannerWindow>(controller,"banner").Close();
            if(runState!=null)
            {
                await Field<SessionStream>(runState,"Stream").DisposeAsync();
                await Field<MicrophoneCapture>(runState,"Capture").DisposeAsync();
                Field<CancellationTokenSource>(runState,"Cancel").Dispose();
            }
            Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",previousHome);
            // Only the exact private synthetic fixture directory is eligible for cleanup.
            var full=Path.GetFullPath(syntheticHome);var temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("LocalDictation-NativeCompletion-",StringComparison.Ordinal))
                if(Directory.Exists(full))Directory.Delete(full,true);
            await FocusTarget(Prefix.Length);
        }
    }

    async Task SelectionTransition(bool persistent,bool changeFocus)
    {
        ResetText();await FocusTarget(Prefix.Length);using var worker=new AutomationWorker();current=await CaptureOwn(worker,this);
        const string text="Native selection transition č. 42 😀";
        var before=ClipboardChecks.Fingerprint();
        var settled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};
        var focusDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
        var pastes=0;
        delay.Tick+=(_,_)=>{delay.Stop();target.Select(Prefix.Length+text.Length,0);settled.TrySetResult();};
        focusDelay.Tick+=(_,_)=>{focusDelay.Stop();other.Focus();other.Select(0,0);};
        KeyEventHandler intercept=(_,e)=>
        {
            if(e.Key==Key.V&&(Keyboard.Modifiers&ModifierKeys.Control)!=0)
            {
                e.Handled=true;pastes++;target.Paste();
                target.Select(Prefix.Length,text.Length);if(!persistent)delay.Start();if(changeFocus)focusDelay.Start();
            }
        };
        target.PreviewKeyDown+=intercept;bool applied=false;
        try
        {
            applied=await worker.Run(token=>current.Apply(text,token));
            if(persistent||changeFocus)
            {
                Require(!applied&&current.IsConflict,"An unstable caret or lost field was acknowledged as a safe result.");
                Require(ClipboardRecovery.HasPending,"An unknown readback discarded its clipboard lease.");
            }
        }
        finally
        {
            target.PreviewKeyDown-=intercept;focusDelay.Stop();
            if(persistent&&pastes>0){target.Select(Prefix.Length+text.Length,0);settled.TrySetResult();}
            if(pastes>0)await settled.Task.WaitAsync(TimeSpan.FromSeconds(2));delay.Stop();
            Require(target.Text==Prefix+text+Suffix,"The controlled selection transition changed surrounding text.");
            if(ClipboardRecovery.HasPending)
            {
                // This fixture owns and has inspected the result after its known transition finished.
                var recovery=ClipboardRecovery.RestoreAfterInspection();
                Require(recovery.Succeeded,"Controlled transition cleanup failed: "+recovery.Message);
            }
        }
        if(!persistent&&!changeFocus)
        {
            Require(applied,"A transient native selection state aborted confirmation: "+current.Reason);
            Require(current.InsertedText==text&&!current.IsConflict,"The settled native result was not owned exactly.");
            Require(current.LastDiagnostic?.ReadbackRetries>0&&current.LastDiagnostic.LastReadbackStage=="selection-collapse","The native regression did not exercise the actual unstable selection readback.");
        }
        Require(pastes==1,"Readback recovery repeated the paste.");
        Require(target.SelectionLength==0&&target.SelectionStart==Prefix.Length+text.Length,"The native transition did not leave the expected caret.");
        Require(other.Text==OtherText,"A provider transition wrote to another field.");
        ClipboardChecks.RequireSame(before,ClipboardChecks.Fingerprint());
    }

    async Task CollapsedSelectionAppend()
    {
        var field=new CollapsedSelectionTextBox{Text=Prefix+Suffix,Margin=new(16),FontSize=16};
        temporaryTarget=new Window{Title="Delivery probe — collapsed selection regression",Width=650,Height=160,
            Owner=this,Content=field,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var before=ClipboardChecks.Fingerprint();
        try
        {
            temporaryTarget.Show();temporaryTarget.Activate();field.Focus();field.Select(Prefix.Length,0);await Settle();
            using var appendWorker=new AutomationWorker();current=await CaptureOwn(appendWorker,temporaryTarget);
            foreach(var text in new[]{Draft,Expanded,Approved})
            {
                var applied=await appendWorker.Run(token=>current.Apply(text,token));
                Require(applied,"Collapsed-selection-sensitive delivery failed: "+current.Reason+
                    $"; collapsed provider Select calls={field.CollapsedProviderSelections}; suppressed paste commands={field.SuppressedPastes}");
                Require(field.Text==Prefix+text+Suffix,"Collapsed-selection-sensitive delivery changed surrounding text or duplicated the draft.");
                Require(field.SelectionLength==0&&field.SelectionStart==Prefix.Length+text.Length,"The append did not retain its exact native caret.");
            }
            Require(field.CollapsedProviderSelections==0&&!field.ProviderPoisoned,"Append selected an already collapsed UIA range.");
            Require(field.SuppressedPastes==0,"A paste was sent after the provider lost its caret.");
            Require(other.Text==OtherText,"Collapsed-selection-sensitive delivery changed the unrelated field.");
        }
        finally
        {
            if(ClipboardRecovery.HasPending)
            {
                // This fixture owns the only target. Its synchronous PreviewKeyDown
                // has consumed the single queued Ctrl+V; no deferred paste exists.
                Require(field.SuppressedPastes>0,"The fixture did not observe consumption of its queued paste; keep its clipboard backup alive.");
                Require(field.Text==Prefix+Suffix,"The poisoned provider unexpectedly inserted text; inspect the fixture before recovery.");
                var recovery=ClipboardRecovery.RestoreAfterInspection();
                Require(recovery.Succeeded,"Collapsed-selection cleanup failed: "+recovery.Message);
            }
            temporaryTarget.Close();temporaryTarget=null;await Settle();
            ClipboardChecks.RequireSame(before,ClipboardChecks.Fingerprint());
        }
    }

    async Task OwnedSuffixWithoutFindText(bool unicode,bool badMovement=false,bool staleAnchor=false)
    {
        var lead=unicode?string.Concat(Enumerable.Repeat("  č. 42 😀 — žĺťučký kôň; technical term  ",40)):"";
        var initial=unicode?lead+"cafe\u0301 👩":Draft;
        var corrected=unicode?lead+"cafe\u0300 👩":badMovement?"pôvodný opravený text":"opravený diktát";
        var appended=unicode?corrected+"\u200d🔬 e":corrected+" pokračuje";
        var accented=unicode?appended+"\u0301":appended;
        var revised=unicode?lead+"cafe\u0300 👩\u200d🚀 e\u0301":"opravený text pokračuje";
        var final=unicode?lead+"overené 🧑\u200d💻 cafe\u0301":Approved;
        // The same long draft occurs on both sides of the actual insertion point.
        var prefix=unicode?"Repeated | "+initial+" | "+initial+" | 🧑\u200d💻":badMovement?Prefix:"Prefix | cafe\u0301";
        var suffix=unicode?" | "+initial+" | "+corrected+" | Repeated":Suffix;
        var field=new CollapsedSelectionTextBox{Text=prefix+suffix,Margin=new(16),FontSize=16};
        var fixture=new Window{Title="Delivery probe — owned suffix regression",Width=780,Height=190,
            Owner=this,Content=field,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        temporaryTarget=fixture;
        var mainBefore=target.Text;var otherBefore=other.Text;
        var clipboardBefore=ClipboardChecks.Fingerprint();
        var acknowledged="";
        using var suffixWorker=new AutomationWorker();
        try
        {
            Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction,"The owned-suffix fixture requires a settled clipboard.");
            fixture.Show();fixture.Activate();field.Focus();field.Select(prefix.Length,0);await Settle();
            var owned=await CaptureOwn(suffixWorker,fixture);current=owned;
            async Task Apply(string label,string text,bool reject=false)
            {
                var pastesBefore=field.PasteCommands;
                var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var applied=false;
                try
                {
                    applied=await suffixWorker.Run(token=>
                    {
                        try{return owned.Apply(text,token);}
                        finally{finished.TrySetResult();}
                    });
                }
                finally
                {
                    if(!applied)owned.Invalidate();
                    await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                if(reject||!applied)
                {
                    Require(!applied,"A provider that did not move its range was accepted.");
                    Require(owned.LastDiagnostic?.QueuedInputEvents==0&&field.PasteCommands==pastesBefore,
                        "Range validation failed after input was already queued.");
                    Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction,
                        "Range validation failure retained a clipboard transaction.");
                    Require(field.Text==prefix+acknowledged+suffix&&field.SelectionLength==0&&field.SelectionStart==prefix.Length+acknowledged.Length,
                        "Range validation failure changed the synthetic destination or caret.");
                    ClipboardChecks.RequireSame(clipboardBefore,ClipboardChecks.Fingerprint());
                    if(!reject)
                    {
                        Require(owned.Reason=="The owned draft range is no longer available."&&field.SuppressedFindTextCalls>0,
                            label+" failed for an unexpected reason: "+owned.Reason);
                        throw new InvalidOperationException(label+" failed before any replacement input: "+owned.Reason+"; queued=0; clipboard settled.");
                    }
                    Require(field.RefusedRangeMovements>0,"The invalid endpoint movement was not exercised.");
                    return;
                }
                Require(owned.InsertedText==text&&!owned.IsConflict,label+" was not acknowledged as the exact owned draft.");
                Require(owned.LastDiagnostic is {ExactText:true,ExactCaret:true}&&owned.LastDiagnostic.QueuedInputEvents==(text.Length==0?2:4),
                    label+" did not receive exact native text/caret acknowledgement.");
                Require(field.Text==prefix+text+suffix,label+" changed surrounding text or duplicated the owned draft.");
                Require(field.SelectionLength==0&&field.SelectionStart==prefix.Length+text.Length,label+" changed the exact native caret.");
                Require(field.PasteCommands==pastesBefore+(text.Length==0?0:1),label+" repeated or omitted its paste command.");
                Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction,label+" left a clipboard transaction pending.");
                Require(target.Text==mainBefore&&other.Text==otherBefore&&other.Text==OtherText,label+" changed an unrelated field.");
                ClipboardChecks.RequireSame(clipboardBefore,ClipboardChecks.Fingerprint());
                acknowledged=text;
            }
            // FindText is available until the first paste has genuinely ACKed.
            await Apply("Initial draft",initial);
            field.SuppressFindText=true;field.RefuseBackwardCharacterMovement=badMovement;field.ThrowStaleRangeClone=staleAnchor;
            await Apply("Corrected draft",corrected,reject:badMovement);
            if(badMovement)return;
            if(staleAnchor)
            {
                Require(field.StaleRangeCloneFailures>0,"The expired pre-paste range was not exercised.");
                Require(field.CollapsedProviderSelections==0&&!field.ProviderPoisoned&&field.SuppressedPastes==0,
                    "Stale-anchor fallback selected a collapsed range or lost the native caret.");
                return;
            }
            await Apply("Appended draft",appended);
            if(unicode)await Apply("Appended combining accent",accented);
            await Apply("Revised draft",revised);
            await Apply("Owned draft withdrawal","");
            await Apply("Final draft",final);
            Require(field.CollapsedProviderSelections==0&&!field.ProviderPoisoned&&field.SuppressedPastes==0,
                "Owned-suffix delivery selected a collapsed range or lost the native caret.");
        }
        finally
        {
            if(ClipboardRecovery.HasPending)
            {
                // This fixture has no deferred paste handler. The completed worker
                // operation and UI continuation have observed its synchronous input.
                Require(field.PasteCommands>0,"No owned paste was observed; keep its clipboard backup alive.");
                var recovery=ClipboardRecovery.RestoreAfterInspection();
                Require(recovery.Succeeded,"Owned-suffix cleanup failed: "+recovery.Message);
            }
            fixture.Close();if(ReferenceEquals(temporaryTarget,fixture))temporaryTarget=null;await Settle();
            Require(target.Text==mainBefore&&other.Text==otherBefore,"Owned-suffix cleanup changed an unrelated field.");
            ClipboardChecks.RequireSame(clipboardBefore,ClipboardChecks.Fingerprint());
        }
    }

    async Task InitialParagraphProjection(string processContext,bool mutateSignature=false)
    {
        const string before="\n\nSIGNATURE | č. 42 😀 | End";
        const string payload="Úplný syntetický diktát č. 42 😀 — cafe\u0301";
        var expected=payload+before[1..]+(mutateSignature?"!":"");
        var field=new ParagraphProjectionTextBox{Text=before,MutateSignature=mutateSignature,AcceptsReturn=true,Margin=new(16),FontSize=16};
        var fixture=new Window{Title="Delivery probe — initial paragraph projection",Width=780,Height=210,
            Owner=this,Content=field,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        temporaryTarget=fixture;
        var mainBefore=target.Text;var otherBefore=other.Text;
        var clipboardBefore=ClipboardChecks.Fingerprint();
        bool? applied=null;var closedBeforeRecovery=false;DeliveryDiagnostic? firstPasteDiagnostic=null;
        using var projectionWorker=new AutomationWorker();
        try
        {
            Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction,
                "The paragraph-projection fixture requires a settled clipboard.");
            fixture.Show();fixture.Activate();field.Focus();field.Select(0,0);await Settle();
            var owned=await CaptureOwn(projectionWorker,fixture);current=owned;
            var guard=Field<TargetGuard>(owned,"guard");
            Require(guard.Start==0&&guard.Caret==0&&guard.OwnedText==""&&guard.ExpectedText==before,
                "The fixture did not capture exactly two leading LF characters at the empty document-start caret.");
            // Only this known fixture-owned delivery receives synthetic application
            // context. The absent field permits publishing the failing old baseline;
            // success below still requires the captured production context to exist.
            var processField=typeof(WindowsTextDelivery).GetField("destinationProcessName",Members);
            processField?.SetValue(owned,processContext);
            var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                applied=await projectionWorker.Run(token=>
                {
                    try{return owned.Apply(payload,token,restoreFocus:true);}
                    finally{finished.TrySetResult();}
                },TimeSpan.FromSeconds(5));
            }
            finally
            {
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            firstPasteDiagnostic=owned.LastDiagnostic;
            Require(field.PasteCommands==1&&field.CompleteNativePastes==1&&field.CoalescedLineFeeds==1,
                "The fixture did not consume exactly one complete native paste before its single-LF projection change.");
            Require(field.FullPasteText==payload+before&&field.FullPasteCaret==payload.Length,
                "The ordinary first native paste did not insert the complete visible payload before paragraph coalescing.");
            Require(field.Text==expected&&field.SelectionLength==0&&field.SelectionStart==payload.Length,
                "The fixture did not preserve the full payload, exact caret and all expected signature text.");
            Require(owned.LastDiagnostic is {Final:true,QueuedInputEvents:4,ExactCaret:true},
                "The final paragraph-projection case did not use one four-event native paste at the exact caret.");
            Require(target.Text==mainBefore&&other.Text==otherBefore&&other.Text==OtherText,
                "The paragraph-projection case changed an unrelated field.");
            if(processContext=="olk"&&!mutateSignature)
            {
                Require(applied==true,"The complete first native paste was left unconfirmed after only the known initial LF projection: "+owned.Reason);
                Require(firstPasteDiagnostic is {ExactText:false,ExactCaret:true,OutlookParagraphProjection:true},
                    "Known paragraph projection was not reported separately from the raw text mismatch.");
                Require(processField!=null,"The successful fixture did not exercise captured production application context.");
                Require(owned.InsertedText==payload&&!owned.IsConflict&&guard.ExpectedText==expected&&guard.Caret==payload.Length,
                    "Known paragraph projection did not adopt the exact observed baseline and owned payload.");
                Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction&&owned.LastDiagnostic?.ClipboardRestored==true,
                    "Known paragraph projection did not restore the original clipboard formats.");
                ClipboardChecks.RequireSame(clipboardBefore,ClipboardChecks.Fingerprint());
                Require(await projectionWorker.Run(token=>owned.Apply(payload,token,restoreFocus:true)),
                    "Repeating the acknowledged final text did not preserve the adopted observed baseline.");
                Require(field.PasteCommands==1&&field.Text==expected,"An acknowledged paragraph projection replayed the paste.");
            }
            else
            {
                Require(firstPasteDiagnostic is {ExactText:false,OutlookParagraphProjection:false},
                    "An unknown native result was reported as the known Outlook paragraph projection.");
                Require(applied==false&&owned.IsConflict&&owned.InsertedText==""&&guard.ExpectedText==before,
                    "An ordinary process or additional signature mutation was acknowledged as a known paragraph projection.");
                Require(ClipboardRecovery.HasPending&&ClipboardRecovery.HasUnsettledTransaction,
                    "An unknown native result discarded its protected clipboard lease.");
                Require(System.Windows.Clipboard.GetText()==payload,"An unknown native result exposed the old clipboard to a queued paste.");
                Require(!await projectionWorker.Run(token=>owned.Apply(payload,token,restoreFocus:true)),
                    "An unknown native result was automatically retried.");
                Require(field.PasteCommands==1&&field.Text==expected,"Refusing an unknown native result replayed the paste.");
            }
        }
        finally
        {
            // This field synchronously consumed Ctrl+V; its full paste and observed
            // result are available above. Close that exact owned target before
            // restoring any held clipboard, including a failing RED baseline.
            fixture.Close();if(ReferenceEquals(temporaryTarget,fixture))temporaryTarget=null;await Settle();
            if(ClipboardRecovery.HasPending)
            {
                closedBeforeRecovery=true;
                Require(field.PasteCommands==1&&field.CompleteNativePastes==1&&field.Text==expected,
                    "The fixture cannot prove consumption of its only paste; keep the protected clipboard backup.");
                var recovery=ClipboardRecovery.RestoreAfterInspection();
                Require(recovery.Succeeded,"Paragraph-projection cleanup failed: "+recovery.Message);
            }
            paragraphProjection=new(processContext,field.PasteCommands,field.CompleteNativePastes,field.CoalescedLineFeeds,
                before,field.FullPasteText,field.Text,field.FullPasteCaret,field.SelectionStart,applied,closedBeforeRecovery,firstPasteDiagnostic);
            Require(target.Text==mainBefore&&other.Text==otherBefore,"Paragraph-projection cleanup changed an unrelated field.");
            ClipboardChecks.RequireSame(clipboardBefore,ClipboardChecks.Fingerprint());
            Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction,
                "Paragraph-projection cleanup left a protected clipboard transaction unsettled.");
        }
    }

    async Task RunChecks()
    {
        run.IsEnabled=false;results.Clear();inputTrace.Clear();captureIds.Clear();inputTraceClock.Restart();
        activeCheck="";captureAttempt=0;captureInProgress=false;captureWindow=0;
        log.Text="";ResetText();observedInputs=ignoredProgrammaticFocus=0;
        var started=DateTimeOffset.UtcNow;
        using var worker=new AutomationWorker();
        try
        {
            await Step("Native clipboard format cloning preserves synthetic memory and GDI resources",()=>{ClipboardChecks.Run();return Task.CompletedTask;});
            await Step("Failed native clipboard restoration retains every original backup format",()=>{ClipboardChecks.RecoveryChecks();return Task.CompletedTask;});
            observer=new GlobalInput{ShortcutEnabled=false,SessionActive=false};
            observer.UserEdited+=input=>
            {
                var owned=current;
                var operationActive=owned==null?(bool?)null:owned.OwnsProgrammaticFocus(owned.Window);
                var ownsFocus=owned==null?(bool?)null:owned.OwnsProgrammaticFocus(input.RootWindow);
                var suspended=false;
                if(owned!=null)
                {
                    observedInputs++;
                    if(input.FocusOnly&&ownsFocus==true)ignoredProgrammaticFocus++;
                    else{owned.Suspend();suspended=true;}
                }
                TraceInput("input",input,operationActive,ownsFocus,suspended);
            };
            await Step("Capture and insert live draft",async()=>
            {
                await FocusTarget(Prefix.Length);current=await CaptureOwn(worker,this);
                var applied=await worker.Run(token=>current.Apply(Draft,token));
                Require(applied,"Initial draft delivery failed: "+current.Reason);
                Require(target.Text==Prefix+Draft+Suffix,"Draft did not preserve prefix and suffix.");
                Require(other.Text==OtherText,"Unrelated field changed.");
            });
            await Step("Pause at another field without writing there",async()=>
            {
                current!.Suspend();await FocusOther();var before=target.Text;
                var applied=await worker.Run(token=>current.Apply(Expanded,token));
                Require(!applied&&!current.IsConflict,"A different focused field should pause without permanent conflict.");
                Require(target.Text==before&&other.Text==OtherText,"Paused delivery changed a field.");
            });
            await Step("Return inside owned draft and resume latest transcript",async()=>
            {
                await FocusTarget(Prefix.Length+2);
                var applied=await worker.Run(token=>current!.Apply(Expanded,token));
                Require(applied,"Return to original range did not resume: "+current!.Reason);
                Require(target.Text==Prefix+Expanded+Suffix,"Resumed draft was duplicated or changed surrounding text.");
                Require(other.Text==OtherText,"Unrelated field changed.");
            });
            await Step("Withdraw only owned draft when Review starts",async()=>
            {
                await ShowReviewSurface();
                var applied=await worker.Run(token=>current!.Apply("",token,restoreFocus:true));
                Require(applied,"Withdrawal failed: "+current!.Reason);
                Require(target.Text==Prefix+Suffix,"Withdrawal changed prefix or suffix, or left a duplicate draft.");
                Require(other.Text==OtherText,"Unrelated field changed.");
                reviewSurface!.Hide();await Settle();
            });
            await Step("Accept reviewed text and restore its original destination",async()=>
            {
                await ShowReviewSurface();
                var applied=await worker.Run(token=>current!.Apply(Approved,token,restoreFocus:true));
                Require(applied,"Reviewed reinsertion failed: "+current!.Reason);
                Require(target.Text==Prefix+Approved+Suffix,"Accepted text was duplicated or changed surrounding text.");
                Require(other.Text==OtherText,"Unrelated field changed.");
                reviewSurface!.Hide();await Settle();
            });
            await Step("Manual document edit creates conflict without overwriting it",async()=>
            {
                target.Text="Manual edit | "+target.Text;await FocusTarget(target.Text.Length);
                var before=target.Text;
                var applied=await worker.Run(token=>current!.Apply("MUST NOT BE INSERTED",token));
                Require(!applied&&current!.IsConflict,"Changed document was not marked as a permanent conflict.");
                Require(target.Text==before&&other.Text==OtherText,"Conflicting delivery changed a field.");
            });
            await Step("Closed original target cannot receive reviewed text",async()=>
            {
                var field=new TextBox{Text="Temporary synthetic target",Margin=new(16)};
                temporaryTarget=new Window{Title="Delivery probe — temporary target",Width=550,Height=140,Owner=this,Content=field,WindowStartupLocation=WindowStartupLocation.CenterOwner};
                temporaryTarget.Show();temporaryTarget.Activate();field.Focus();field.Select(field.Text.Length,0);await Settle();
                current=await CaptureOwn(worker,temporaryTarget);
                temporaryTarget.Close();temporaryTarget=null;await FocusOther();var before=target.Text;
                var applied=await worker.Run(token=>current.Apply("MUST NOT BE INSERTED",token,restoreFocus:true));
                Require(!applied&&current.IsConflict,"Closed target was not rejected.");
                Require(target.Text==before&&other.Text==OtherText,"Missing-target delivery wrote into a surviving field.");
            });
            await Step("Controller completion inserts final text at the real captured cursor",async()=>
            {
                await ControllerCompletion(worker,verify:false);
            },continueAfterFailure:true);
            await Step("Controller verification agreement inserts at the real captured cursor",async()=>
            {
                await ControllerCompletion(worker,verify:true);
            },continueAfterFailure:true);
            await Step("Transient post-paste selection settles before exact native acknowledgement",()=>SelectionTransition(persistent:false,changeFocus:false));
            await Step("Persistent post-paste selection cannot bypass exact caret acknowledgement",()=>SelectionTransition(persistent:true,changeFocus:false));
            await Step("Focus loss during unstable readback cannot adopt another field",()=>SelectionTransition(persistent:false,changeFocus:true));
            await Step("Long Unicode live delivery fits the original two-second worker deadline",async()=>
            {
                ResetText();await FocusTarget(Prefix.Length);current=await CaptureOwn(worker,this);
                var before=ClipboardChecks.Fingerprint();
                var text=string.Concat(Enumerable.Repeat("  č. 42 😀 — žĺťučký kôň; technical term  ",80));
                var applied=await worker.Run(token=>current.Apply(text,token));
                Require(applied,"Long live delivery failed: "+current.Reason);
                Require(target.Text==Prefix+text+Suffix,"Long live delivery changed Unicode or surrounding text.");
                Require(current.LastDiagnostic?.QueuedInputEvents==4,"Live text was not delivered in one guarded paste burst.");
                ClipboardChecks.RequireSame(before,ClipboardChecks.Fingerprint());
            });
            await Step("Long Unicode final delivery preserves the entire draft",async()=>
            {
                ResetText();await FocusTarget(Prefix.Length);current=await CaptureOwn(worker,this);
                var unicode=string.Concat(Enumerable.Repeat("  č. 42 😀 — žĺťučký kôň; technical term  ",80));
                using var longWorker=new AutomationWorker();
                var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var clipboardBefore=ClipboardChecks.Fingerprint();
                bool applied=false;
                try{applied=await longWorker.Run(token=>{try{return current.Apply(unicode,token,restoreFocus:true);}finally{finished.TrySetResult();}});}
                finally{if(!applied)current.Invalidate();await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));await Settle();}
                Require(applied,"Long Unicode delivery failed: "+current.Reason);
                ClipboardChecks.RequireSame(clipboardBefore,ClipboardChecks.Fingerprint());
                Require(current.LastDiagnostic?.QueuedInputEvents==4,"A complete final draft was not delivered in one paste burst.");
                Require(target.Text==Prefix+unicode+Suffix,"Long Unicode delivery lost or changed text.");
                Require(other.Text==OtherText,"Unrelated field changed.");
            },continueAfterFailure:true);
            await Step("Rich-text final delivery preserves formatting and surrounding text",async()=>
            {
                var field=new RichTextBox{Margin=new(16),FontSize=16};
                var paragraph=new Paragraph();paragraph.Inlines.Add(new Bold(new Run(Prefix)));paragraph.Inlines.Add(new Italic(new Run(Suffix)));
                field.Document=new FlowDocument(paragraph);
                var initialRichText=new TextRange(field.Document.ContentStart,field.Document.ContentEnd).Text;
                Require(initialRichText==Prefix+Suffix+"\r\n","The synthetic rich-text baseline is invalid: "+JsonSerializer.Serialize(initialRichText));
                temporaryTarget=new Window{Title="Delivery probe — synthetic rich-text target",Width=780,Height=210,Owner=this,Content=field,WindowStartupLocation=WindowStartupLocation.CenterOwner};
                temporaryTarget.Show();temporaryTarget.Activate();field.Focus();
                var prefixRun=((Bold)paragraph.Inlines.FirstInline).Inlines.FirstInline;
                field.CaretPosition=prefixRun.ContentEnd;await Settle();using var richWorker=new AutomationWorker();current=await CaptureOwn(richWorker,temporaryTarget);
                var applied=await richWorker.Run(token=>current.Apply(Approved,token,restoreFocus:true));
                Require(applied,"Rich-text delivery failed: "+current.Reason);
                var observedRichText=new TextRange(field.Document.ContentStart,field.Document.ContentEnd).Text;
                Require(observedRichText==Prefix+Approved+Suffix+"\r\n","Rich-text delivery changed surrounding text: "+JsonSerializer.Serialize(observedRichText));
                Require(paragraph.Inlines.OfType<Bold>().Any(),"Rich-text delivery removed existing formatting.");
                Require(other.Text==OtherText,"Unrelated field changed.");
                temporaryTarget.Close();temporaryTarget=null;await Settle();
            });
            await Step("A delayed native paste keeps its own payload until deliberate clipboard recovery",async()=>
            {
                ResetText();await FocusTarget(Prefix.Length);using var delayedWorker=new AutomationWorker();current=await CaptureOwn(delayedWorker,this);
                var before=ClipboardChecks.Fingerprint();
                const string delayedText="Delayed synthetic payload č. 42 😀";
                var pasted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1600)};
                delay.Tick+=(_,_)=>{delay.Stop();target.Paste();pasted.TrySetResult();};
                KeyEventHandler intercept=(_,e)=>
                {
                    if(e.Key==Key.V&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){e.Handled=true;delay.Start();}
                };
                target.PreviewKeyDown+=intercept;
                try
                {
                    var applied=await delayedWorker.Run(token=>current.Apply(delayedText,token,restoreFocus:true),TimeSpan.FromSeconds(5));
                    Require(!applied&&ClipboardRecovery.HasPending,"An unconfirmed queued paste restored/discarded its clipboard lease.");
                    Require(System.Windows.Clipboard.GetText()==delayedText,"Unknown paste exposed unrelated restored clipboard text.");
                    await pasted.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(target.Text==Prefix+delayedText+Suffix,"The deferred native paste consumed unrelated clipboard data.");
                    Require(!await delayedWorker.Run(token=>current.Apply(delayedText,token,true)),"An unknown completed paste was automatically repeated.");
                    Require(target.Text==Prefix+delayedText+Suffix,"Retry duplicated the delayed text.");
                }
                finally
                {
                    target.PreviewKeyDown-=intercept;delay.Stop();
                    // The fixture has now inspected the known destination and stopped its deferred paste.
                    var recovery=ClipboardRecovery.RestoreAfterInspection();
                    Require(recovery.Succeeded,"Deliberate recovery failed: "+recovery.Message);
                }
                ClipboardChecks.RequireSame(before,ClipboardChecks.Fingerprint());
            });
            await Step("Append preserves an existing caret without selecting its collapsed UIA range",CollapsedSelectionAppend,continueAfterFailure:true);
            await Step("Acknowledged draft correction, append and withdrawal survive unavailable FindText",()=>OwnedSuffixWithoutFindText(unicode:false),continueAfterFailure:true);
            await Step("Repeated surroundings and long Unicode revisions survive unavailable FindText",()=>OwnedSuffixWithoutFindText(unicode:true),continueAfterFailure:true);
            await Step("Invalid provider endpoint movement cannot queue replacement input",()=>OwnedSuffixWithoutFindText(unicode:false,badMovement:true),continueAfterFailure:true);
            await Step("Expired pre-paste anchor falls back to a fresh exact caret range",()=>OwnedSuffixWithoutFindText(unicode:false,staleAnchor:true),continueAfterFailure:true);
            await Step("Known Outlook initial LF projection confirms the first full final paste without replay",()=>InitialParagraphProjection("olk"),continueAfterFailure:true);
            await Step("Ordinary process initial LF mutation retains clipboard recovery without replay",()=>InitialParagraphProjection("ordinary-fixture"),continueAfterFailure:true);
            await Step("Additional signature mutation in Outlook retains clipboard recovery without replay",()=>InitialParagraphProjection("olk",mutateSignature:true),continueAfterFailure:true);
            var passed=results.All(r=>r.Passed);status.Text=$"{(passed?"PASS":"FAIL")} — {results.Count(r=>r.Passed)}/{results.Count} native delivery checks";status.Foreground=passed?Brushes.DarkGreen:Brushes.DarkRed;
        }
        catch(Exception exception)
        {
            if(results.LastOrDefault()?.Passed!=false)
            {
                results.Add(new("Probe initialization",false,0,exception.ToString(),target.Text,other.Text,current?.IsConflict,current?.LastDiagnostic));
                log.Text+="FAIL  Probe initialization\n"+exception+"\n";
            }
            status.Text=$"FAIL — {results.Last().Name}";status.Foreground=Brushes.DarkRed;
        }
        finally
        {
            current=null;observer?.Dispose();observer=null;reviewSurface?.Hide();temporaryTarget?.Close();temporaryTarget=null;
            var report=new{StartedUtc=started,CompletedUtc=DateTimeOffset.UtcNow,Fixture="Local Dictation DeliveryProbe",NativeProductCode=true,
                SyntheticFieldsOnly=true,ClipboardRecoveryPending=ClipboardRecovery.HasPending,ClipboardTransactionUnsettled=ClipboardRecovery.HasUnsettledTransaction,Passed=results.Count==26&&results.All(r=>r.Passed),ObservedInputs=observedInputs,
                IgnoredProgrammaticFocus=ignoredProgrammaticFocus,InputTrace=inputTrace,Results=results};
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                log.Text+="Report: "+reportPath;
            }
            catch(Exception exception){status.Text="FAIL — could not save report";status.Foreground=Brushes.DarkRed;log.Text+="Report error: "+exception;}
            run.IsEnabled=true;
        }
    }
}
