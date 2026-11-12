using System.IO;
using System.Reflection;
using System.Text.Json;
using LocalDictation.App.Services;

namespace LocalDictation.DeliveryProbe;

// Pure file IO only. This entry point must run before any WPF Application exists.
static class DiagnosticChecks
{
    const string Flag="LOCAL_DICTATION_DELIVERY_DIAGNOSTICS",Home="LOCAL_DICTATION_HOME";
    const int Limit=256*1024;
    static DeliveryDiagnostic Sample()=>new("validate",false,0,1,0,0,null,null,null,
        null,null,null,null,null,null,"fixture",null,null,null,false);

    public static void Run(string report)
    {
        var originalFlag=Environment.GetEnvironmentVariable(Flag);
        var originalHome=Environment.GetEnvironmentVariable(Home);
        var parent=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"LocalDictation-capture-diagnostic-checks"));
        var root=Path.GetFullPath(Path.Combine(parent,Guid.NewGuid().ToString("N")));
        var results=new List<object>();
        void Check(string name,Action run)
        {
            try{run();results.Add(new{Name=name,Passed=true});}
            catch(Exception error){Environment.ExitCode=1;results.Add(new{Name=name,Passed=false,Failure=error.GetBaseException().Message});}
        }
        try
        {
            Require(root.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Unsafe temporary root.");
            Environment.SetEnvironmentVariable(Home,root);
            Check("Opt-out creates no diagnostic files",()=>
            {
                foreach(var disabled in new string?[]{null,"0","true"})
                {
                    Environment.SetEnvironmentVariable(Flag,disabled);
                    Sample().WriteIfEnabled();Session();Capture(false);
                    Require(!Directory.Exists(root),"Disabled diagnostics created a directory.");
                }
            });
            Environment.SetEnvironmentVariable(Flag,"1");
            Check("Enabled startup marker identifies current process and build",()=>
            {
                Session();using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"logs","delivery-session.json")));
                Require(json.RootElement.GetProperty("ProcessId").GetInt32()==Environment.ProcessId,"Wrong session PID.");
                Require(!string.IsNullOrWhiteSpace(json.RootElement.GetProperty("BuildVersion").GetString()),"Missing build version.");
                Require(json.RootElement.GetProperty("Time").GetDateTimeOffset()<=DateTimeOffset.UtcNow,"Invalid session time.");
            });
            Check("Delivery events include current process identifier",()=>
            {
                Sample().WriteIfEnabled();using var json=JsonDocument.Parse(File.ReadLines(Path.Combine(root,"logs","delivery-metadata.jsonl")).Last());
                Require(json.RootElement.GetProperty("ProcessId").GetInt32()==Environment.ProcessId,"Wrong event PID.");
                Require(json.RootElement.GetProperty("Delivery").GetProperty("Phase").GetString()=="validate","Changed existing delivery payload.");
            });
            Check("Delivery interruptions serialize fixed stage without private content",()=>
            {
                var property=typeof(DeliveryDiagnostic).GetProperty("LastInterruptionStage");
                Require(property!=null,"Interruption stage metadata is not implemented.");
                var interrupted=typeof(WindowsTextDelivery).GetNestedType("Interrupted",BindingFlags.NonPublic)!;
                var constructor=interrupted.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Single();
                var stageType=constructor.GetParameters().Single().ParameterType;
                Require(stageType.IsEnum,"Interruption stages accept free-form input.");
                foreach(var (name,stage) in new[]{("Invalidated","invalidated"),("InputGeneration","input-generation"),
                    ("ForegroundWindow","foreground-window"),("FocusedField","focused-field"),("DeliveryKeysDown","delivery-keys-down")})
                {
                    var error=(Exception)constructor.Invoke([Enum.Parse(stageType,name)]);
                    error.Data["PRIVATE-SENTINEL"]="PRIVATE-SENTINEL";
                    var encoded=interrupted.GetProperty("Stage")!.GetValue(error);
                    Require((string?)encoded==stage,"Incorrect fixed interruption stage.");
                    var sample=Sample() with{FailureType=error.GetType().Name,FailureCode=error.HResult};
                    property!.SetValue(sample,encoded);sample.WriteIfEnabled();
                    var line=File.ReadLines(Path.Combine(root,"logs","delivery-metadata.jsonl")).Last();
                    using var json=JsonDocument.Parse(line);
                    Require(json.RootElement.GetProperty("Delivery").GetProperty("LastInterruptionStage").GetString()==stage,"Interruption stage did not round-trip.");
                    Require(!line.Contains("PRIVATE-SENTINEL",StringComparison.Ordinal),"Private interruption content leaked.");
                }
            });
            Check("Capture serializes only bounded technical metadata",()=>
            {
                Capture(true);
                var lines=File.ReadAllLines(Path.Combine(root,"logs","capture-metadata.jsonl"));
                Require(lines.Length>=4,"Missing stage evidence.");
                foreach(var line in lines)
                {
                    using var json=JsonDocument.Parse(line);var item=json.RootElement;
                    Require(item.GetProperty("ProcessId").GetInt32()==Environment.ProcessId,"Wrong capture PID.");
                    Require(item.GetProperty("Milliseconds").GetDouble()>=0,"Invalid elapsed time.");
                    Require(item.GetProperty("Time").GetDateTimeOffset()<=DateTimeOffset.UtcNow,"Invalid capture time.");
                    Require(new[]{"foreground","focused-element"}.Contains(item.GetProperty("Stage").GetString()),"Unexpected capture stage.");
                    Require(item.EnumerateObject().All(p=>new[]{"Time","ProcessId","Stage","Success","Milliseconds","FailureType","FailureCode"}.Contains(p.Name)),"Unexpected capture data field.");
                    Require(!line.Contains("PRIVATE-SENTINEL",StringComparison.Ordinal),"Exception content leaked.");
                }
                using var last=JsonDocument.Parse(lines.Last());
                Require(!last.RootElement.GetProperty("Success").GetBoolean(),"Failure was not recorded.");
                Require(last.RootElement.GetProperty("FailureType").GetString()==nameof(InvalidOperationException),"Missing exception type.");
                Require(last.RootElement.GetProperty("FailureCode").GetInt32()==new InvalidOperationException().HResult,"Missing HRESULT.");
                using var entered=JsonDocument.Parse(lines[0]);using var finished=JsonDocument.Parse(lines[1]);
                Require(entered.RootElement.GetProperty("Success").ValueKind==JsonValueKind.Null,"Missing stage entry.");
                Require(finished.RootElement.GetProperty("Success").GetBoolean(),"Missing stage completion.");
                Capture(false);
                using var completed=JsonDocument.Parse(File.ReadLines(Path.Combine(root,"logs","capture-metadata.jsonl")).Last());
                Require(completed.RootElement.GetProperty("Stage").GetString()=="complete"&&completed.RootElement.GetProperty("Success").GetBoolean(),"Missing capture completion.");
            });
            Check("Capture and delivery journals rotate within 256 KiB",()=>
            {
                foreach(var name in new[]{"capture-metadata.jsonl","delivery-metadata.jsonl"})
                {
                    var path=Path.Combine(root,"logs",name);File.WriteAllText(path,new string('x',Limit-1));
                    if(name.StartsWith("capture",StringComparison.Ordinal))Capture(false);else Sample().WriteIfEnabled();
                    Require(File.Exists(path+".previous"),"Missing rotated journal: "+name);
                    Require(new FileInfo(path).Length<=Limit,"Active journal exceeded bound: "+name);
                    Require(new FileInfo(path+".previous").Length<=Limit,"Previous journal exceeded bound: "+name);
                }
            });
            Check("IO failures leave caller operation and exception unchanged",()=>
            {
                var blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"not a directory");
                Environment.SetEnvironmentVariable(Home,blocked);
                var original=new InvalidOperationException("PRIVATE-SENTINEL");var completed=false;
                Session();Sample().WriteIfEnabled();Capture(true);completed=true;
                Require(completed,"Diagnostic IO changed caller completion.");
                var observed=ObserveException(original);
                Require(ReferenceEquals(observed,original),"Diagnostic IO replaced original exception.");
                Require(File.ReadAllText(blocked)=="not a directory","Diagnostic IO changed blocked file.");
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(Flag,originalFlag);
            Environment.SetEnvironmentVariable(Home,originalHome);
            // Validate both absolute boundaries immediately before recursive cleanup.
            var resolved=Path.GetFullPath(root);
            if(resolved.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&
                Path.GetDirectoryName(resolved)==parent&&Directory.Exists(resolved))Directory.Delete(resolved,true);
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report,JsonSerializer.Serialize(new{Passed=Environment.ExitCode==0,PureFileIOOnly=true,ProcessId=Environment.ProcessId,Checks=results},new JsonSerializerOptions{WriteIndented=true}));
        }
    }

    static void Session()
    {
        var method=typeof(DeliveryDiagnostic).GetMethod("WriteSessionIfEnabled",BindingFlags.Public|BindingFlags.Static);
        Require(method!=null,"Startup diagnostic marker is not implemented.");method!.Invoke(null,null);
    }
    static object NewCapture()
    {
        var type=typeof(DeliveryDiagnostic).Assembly.GetType("LocalDictation.App.Services.CaptureDiagnostic");
        Require(type!=null,"Capture diagnostic journal is not implemented.");
        return type!.GetMethod("Begin")!.Invoke(null,null)!;
    }
    static void Stage(object capture,string stage)
    {
        var method=capture.GetType().GetMethod("Stage")!;
        method.Invoke(capture,[Enum.Parse(method.GetParameters()[0].ParameterType,stage)]);
    }
    static void Capture(bool fail)
    {
        var capture=NewCapture();Stage(capture,"Foreground");Stage(capture,"FocusedElement");
        if(fail)capture.GetType().GetMethod("Fail")!.Invoke(capture,[new InvalidOperationException("PRIVATE-SENTINEL")]);
        else capture.GetType().GetMethod("Complete")!.Invoke(capture,null);
    }
    static Exception ObserveException(Exception original)
    {
        var capture=NewCapture();
        try{throw original;}
        catch(Exception error){capture.GetType().GetMethod("Fail")!.Invoke(capture,[error]);return error;}
    }
    static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
}
