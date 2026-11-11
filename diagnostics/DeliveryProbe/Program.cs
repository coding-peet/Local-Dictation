using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LocalDictation.App.Services;

namespace LocalDictation.DeliveryProbe;

static class Program
{
    [STAThread] static void Main(string[] args)
    {
        var report=args.Length==2&&args[0]=="--report"?Path.GetFullPath(args[1]):
            Path.Combine(@"C:\AI\LocalDictation\diagnostics","delivery-probe-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");
        var application=new System.Windows.Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
        application.Run(new ProbeWindow(report));
    }
}

sealed record StepResult(string Name,bool Passed,double Milliseconds,string Detail,string Target,string Other,bool? Conflict);

sealed class ProbeWindow : Window
{
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
    async Task Step(string name,Func<Task> check)
    {
        status.Text="Running: "+name;var watch=Stopwatch.StartNew();
        try
        {
            await check();results.Add(new(name,true,watch.Elapsed.TotalMilliseconds,"Passed",target.Text,other.Text,current?.IsConflict));
            log.Text+=$"PASS  {name} ({watch.Elapsed.TotalMilliseconds:F0} ms)\n";
        }
        catch(Exception exception)
        {
            results.Add(new(name,false,watch.Elapsed.TotalMilliseconds,exception.ToString(),target.Text,other.Text,current?.IsConflict));
            log.Text+=$"FAIL  {name}\n{exception}\n";throw;
        }
    }

    async Task RunChecks()
    {
        run.IsEnabled=false;results.Clear();log.Text="";ResetText();observedInputs=ignoredProgrammaticFocus=0;
        var started=DateTimeOffset.UtcNow;
        using var worker=new AutomationWorker();
        try
        {
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
            status.Text=$"PASS — {results.Count} native delivery checks";status.Foreground=Brushes.DarkGreen;
        }
        catch(Exception exception)
        {
            if(results.LastOrDefault()?.Passed!=false)
            {
                results.Add(new("Probe initialization",false,0,exception.ToString(),target.Text,other.Text,current?.IsConflict));
                log.Text+="FAIL  Probe initialization\n"+exception+"\n";
            }
            status.Text=$"FAIL — {results.Last().Name}";status.Foreground=Brushes.DarkRed;
        }
        finally
        {
            current=null;observer?.Dispose();observer=null;reviewSurface?.Hide();temporaryTarget?.Close();temporaryTarget=null;
            var report=new{StartedUtc=started,CompletedUtc=DateTimeOffset.UtcNow,Fixture="Local Dictation DeliveryProbe",NativeProductCode=true,
                SyntheticFieldsOnly=true,Passed=results.Count==7&&results.All(r=>r.Passed),ObservedInputs=observedInputs,
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
