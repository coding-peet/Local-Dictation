using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using LocalDictation.App.Services;
using LocalDictation.Core;

namespace LocalDictation.BrowserDeliveryProbe;

static class Program
{
    const BindingFlags StaticMembers=BindingFlags.Static|BindingFlags.NonPublic;
    const string Payload="Native browser paste č. 42 😀";
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    sealed record Options(nint Window,string Nonce,string Control,string Report,int ArmMilliseconds)
    {
        public string Name=>"Local Dictation fixture "+Control+" "+Nonce;
        public string Id=>"ld-fixture-"+Control+"-"+Nonce;
        public string Prefix=>"["+Control+":"+Nonce+"] before | ";
        public string Baseline=>Prefix+" | after";
        public string RecoverySignal=>Report+".restore";
    }
    sealed class Refused(string stage):Exception(stage);
    sealed class Result
    {
        public int ProcessId {get;}=Environment.ProcessId;
        public bool SyntheticFixtureOnly {get;}=true;
        public bool PublicCaptureUsed {get;}=false;
        public bool ScopedProductionInspectUsed {get;set;}
        public string Stage {get;set;}="arming";
        public string Control {get;set;}="";
        public bool Passed {get;set;}
        public bool Refused {get;set;}
        public string? FailureType {get;set;}
        public int? FailureCode {get;set;}
        public bool ScopeValidated {get;set;}
        public bool AutomationIdExact {get;set;}
        public bool ExactSyntheticBaseline {get;set;}
        public bool ExactSyntheticResult {get;set;}
        public int? BeforeLength {get;set;}
        public int? BeforeCaret {get;set;}
        public int? AfterLength {get;set;}
        public bool ClipboardRecoveryPending {get;set;}
        public bool ClipboardTransactionUnsettled {get;set;}
        public string? RecoverySignal {get;set;}
        public DeliveryDiagnostic? Diagnostic {get;set;}
    }

    [STAThread] static int Main(string[] args)
    {
        Options options;
        try
        {
            var values=new Dictionary<string,string>(StringComparer.Ordinal);
            for(var i=0;i<args.Length;i+=2){if(i+1>=args.Length)throw new ArgumentException();values.Add(args[i],args[i+1]);}
            var nonce=values["--nonce"];var control=values["--control"];
            if(!Regex.IsMatch(nonce,"^[0-9a-f]{32}$")||control is not("textarea" or "contenteditable"))throw new ArgumentException();
            options=new(new nint(long.Parse(values["--expected-window"])),nonce,control,Path.GetFullPath(values["--report"]),
                values.TryGetValue("--arm-ms",out var milliseconds)?int.Parse(milliseconds):5000);
            if(options.Window==0||options.ArmMilliseconds is <0 or >30000)throw new ArgumentException();
            Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",Path.Combine(Path.GetDirectoryName(options.Report)!,"isolated-"+nonce));
        }
        catch(Exception){return 2;}
        var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var code=1;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async()=>
        {
            try{code=await Run(options);}finally{app.Shutdown();}
        }));
        Dispatcher.Run();return code;
    }
    static void Write(Options options,Result result)
    {
        result.ClipboardRecoveryPending=ClipboardRecovery.HasPending;
        result.ClipboardTransactionUnsettled=ClipboardRecovery.HasUnsettledTransaction;
        result.RecoverySignal=result.ClipboardTransactionUnsettled?options.RecoverySignal:null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.Report)!);
            File.WriteAllText(options.Report,JsonSerializer.Serialize(result,Json));
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){}
        // A report I/O failure must not terminate an in-memory clipboard backup.
    }
    static AutomationElement Scope(Options options,Result result)
    {
        // No field text is read until every external-surface predicate passes.
        if(WindowsTextDelivery.ForegroundWindow!=options.Window)throw new Refused("foreground-window");
        var element=AutomationElement.FocusedElement??throw new Refused("focused-element");
        var current=element.Current;
        var process=Process.GetProcessById(current.ProcessId).ProcessName;
        if(process is not("chrome" or "msedge")||current.FrameworkId!="Chrome")throw new Refused("browser-process-framework");
        if(current.Name!=options.Name)throw new Refused("exact-nonce-aria-label");
        result.AutomationIdExact=current.AutomationId==options.Id;
        if(!result.AutomationIdExact)throw new Refused("exact-nonce-automation-id");
        var title=new StringBuilder(512);GetWindowText(options.Window,title,title.Capacity);
        if(!title.ToString().StartsWith("Local Dictation native browser fixture "+options.Nonce,StringComparison.Ordinal))throw new Refused("fixture-window-title");
        if(WindowsTextDelivery.ForegroundWindow!=options.Window)throw new Refused("foreground-window-raced");
        result.ScopeValidated=true;return element;
    }
    static string Identity(AutomationElement element)=>element.Current.ProcessId+":"+string.Join(".",element.GetRuntimeId());
    static WindowsTextDelivery CaptureScoped(Options options,Result result,CancellationToken token)
    {
        var element=Scope(options,result);var identity=Identity(element);
        var integrity=typeof(WindowsTextDelivery).GetMethod("Integrity",StaticMembers)!;
        if(!TargetPrivileges.CanInsert((int)integrity.Invoke(null,[element.Current.ProcessId])!,
            (int)integrity.Invoke(null,[Environment.ProcessId])!))throw new Refused("elevated-destination");
        var inspect=typeof(WindowsTextDelivery).GetMethod("Inspect",StaticMembers)!;
        var state=((string Text,int Caret,TextPattern Pattern,System.Windows.Automation.Text.TextPatternRange Selection))inspect.Invoke(null,[element,null])!;
        result.ScopedProductionInspectUsed=true;result.BeforeLength=state.Text.Length;result.BeforeCaret=state.Caret;
        result.ExactSyntheticBaseline=state.Text==options.Baseline||state.Text==options.Baseline+"\r\n"||state.Text==options.Baseline+"\n";
        if(!result.ExactSyntheticBaseline||state.Caret!=options.Prefix.Length)throw new Refused("exact-baseline-caret");
        token.ThrowIfCancellationRequested();
        if(Identity(Scope(options,result))!=identity)throw new Refused("field-identity-raced");
        return (WindowsTextDelivery)Activator.CreateInstance(typeof(WindowsTextDelivery),BindingFlags.Instance|BindingFlags.NonPublic,null,
            [options.Window,new TargetGuard(identity,state.Text,state.Caret),element],null)!;
    }
    static async Task<int> Run(Options options)
    {
        var result=new Result{Control=options.Control};WindowsTextDelivery? delivery=null;GlobalInput? observer=null;
        using var worker=new AutomationWorker();
        try
        {
            Write(options,result);await Task.Delay(options.ArmMilliseconds);
            result.Stage="scoped-capture";
            delivery=await worker.Run(token=>CaptureScoped(options,result,token));
            observer=new GlobalInput{ShortcutEnabled=false,SessionActive=false};
            observer.UserEdited+=change=>{if(!change.FocusOnly||!delivery.OwnsProgrammaticFocus(change.RootWindow))delivery.Suspend();};
            result.Stage="single-native-apply";
            result.Passed=await worker.Run(token=>delivery.Apply(Payload,token),TimeSpan.FromSeconds(5));
            result.Diagnostic=delivery.LastDiagnostic;
            result.Stage="scoped-result-readback";
            result.ExactSyntheticResult=await worker.Run(_=>
            {
                var element=Scope(options,result);
                if(!element.TryGetCurrentPattern(TextPattern.Pattern,out var value))throw new Refused("result-text-pattern");
                var text=((TextPattern)value).DocumentRange.GetText(100001);result.AfterLength=text.Length;
                var expected=options.Prefix+Payload+" | after";
                return text==expected||text==expected+"\r\n"||text==expected+"\n";
            });
            result.Passed&=result.ExactSyntheticResult&&result.Diagnostic?.QueuedInputEvents==4;
            result.Stage=result.Passed?"complete":"unconfirmed";
        }
        catch(Exception error)
        {
            var actual=error is TargetInvocationException{InnerException:{} inner}?inner:error;
            if(actual is Refused refused){result.Refused=true;result.Stage="refused-"+refused.Message;}
            result.FailureType=actual.GetType().Name;result.FailureCode=actual.HResult;result.Diagnostic=delivery?.LastDiagnostic;result.Passed=false;
        }
        finally{observer?.Dispose();}
        Write(options,result);
        // Do not terminate a process that still owns an active or held clipboard backup.
        while(ClipboardRecovery.HasUnsettledTransaction)
        {
            result.Stage=ClipboardRecovery.HasPending?"awaiting-explicit-recovery":"native-provider-still-active";Write(options,result);
            try
            {
                if(ClipboardRecovery.HasPending&&File.Exists(options.RecoverySignal)&&new FileInfo(options.RecoverySignal).Length<=128)
                {
                    if((await File.ReadAllTextAsync(options.RecoverySignal)).Trim()==options.Nonce)
                    {
                        var recovered=await Task.Run(ClipboardRecovery.RestoreAfterInspection);
                        File.Delete(options.RecoverySignal);
                        result.Stage=recovered.Succeeded?"explicit-recovery-complete":"explicit-recovery-failed";Write(options,result);
                    }
                }
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            {result.Stage="recovery-signal-io-failed";Write(options,result);}
            await Task.Delay(250);
        }
        result.Stage=result.Passed?"complete":result.Refused?result.Stage:"failed-without-held-backup";Write(options,result);
        return result.Passed?0:1;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(nint window,StringBuilder text,int maximum);
}
