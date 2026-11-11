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

sealed record StepResult(string Name,bool Passed,double Milliseconds,string Detail,string Target,string Other,bool? Conflict,DeliveryDiagnostic? Diagnostic);

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
        var captured=await worker.Run(token=>WindowsTextDelivery.Capture(token,allowOwnProcess:true));
        if(captured.Window!=expected)
        {captured.Invalidate();throw new InvalidOperationException("Capture escaped the fixture window; no insertion was attempted.");}
        return captured;
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
        status.Text="Running: "+name;var watch=Stopwatch.StartNew();
        try
        {
            await check();results.Add(new(name,true,watch.Elapsed.TotalMilliseconds,"Passed",target.Text,other.Text,current?.IsConflict,current?.LastDiagnostic));
            log.Text+=$"PASS  {name} ({watch.Elapsed.TotalMilliseconds:F0} ms)\n";
        }
        catch(Exception exception)
        {
            results.Add(new(name,false,watch.Elapsed.TotalMilliseconds,exception.ToString(),target.Text,other.Text,current?.IsConflict,current?.LastDiagnostic));
            log.Text+=$"FAIL  {name}\n{exception}\n";if(!continueAfterFailure)throw;
        }
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

    async Task RunChecks()
    {
        run.IsEnabled=false;results.Clear();log.Text="";ResetText();observedInputs=ignoredProgrammaticFocus=0;
        var started=DateTimeOffset.UtcNow;
        using var worker=new AutomationWorker();
        try
        {
            await Step("Native clipboard format cloning preserves synthetic memory and GDI resources",()=>{ClipboardChecks.Run();return Task.CompletedTask;});
            await Step("Failed native clipboard restoration retains every original backup format",()=>{ClipboardChecks.RecoveryChecks();return Task.CompletedTask;});
            observer=new GlobalInput{ShortcutEnabled=false,SessionActive=false};
            observer.UserEdited+=input=>
            {
                var owned=current;if(owned==null)return;
                observedInputs++;
                if(input.FocusOnly&&owned.OwnsProgrammaticFocus(input.RootWindow)){ignoredProgrammaticFocus++;return;}
                owned.Suspend();
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
                SyntheticFieldsOnly=true,Passed=results.Count==15&&results.All(r=>r.Passed),ObservedInputs=observedInputs,
                IgnoredProgrammaticFocus=ignoredProgrammaticFocus,Results=results};
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
