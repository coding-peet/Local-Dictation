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
                    ("ForegroundWindow","foreground-window"),("FocusedField","focused-field"),("DeliveryKeysDown","delivery-keys-down"),("WindowScope","window-scope")})
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
            Check("Focus mismatch metadata exposes only existing process IDs, window handles and bounded hashes",()=>
            {
                var method=typeof(DeliveryDiagnostic).GetMethod("DescribeFocusMismatch",BindingFlags.Public|BindingFlags.Static);
                var property=typeof(DeliveryDiagnostic).GetProperty("FocusMismatch");
                Require(method!=null&&property!=null,"Focus mismatch metadata is not implemented.");
                const string expectedIdentity="4321:42.65.7.PRIVATE-FOCUS-EXPECTED";
                const string observedIdentity="8765:42.65.8.PRIVATE-FOCUS-OBSERVED";
                JsonElement Describe(string expected,string? observed,long expectedWindow,long observedWindow)
                {
                    var metadata=method!.Invoke(null,[expected,observed,(nint)expectedWindow,(nint)observedWindow]);
                    Require(metadata!=null,"Enabled focus mismatch metadata was absent.");
                    var sample=Sample();property!.SetValue(sample,metadata);sample.WriteIfEnabled();
                    var line=File.ReadLines(Path.Combine(root,"logs","delivery-metadata.jsonl")).Last();
                    Require(!line.Contains(expectedIdentity,StringComparison.Ordinal)&&!line.Contains(observedIdentity,StringComparison.Ordinal)&&
                        !line.Contains("PRIVATE-FOCUS",StringComparison.Ordinal),"Raw focus identity leaked.");
                    using var json=JsonDocument.Parse(line);var result=json.RootElement.GetProperty("Delivery").GetProperty("FocusMismatch");
                    Require(result.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(new[]{
                        "ExpectedProcessId","ObservedProcessId","ExpectedIdentityHash","ObservedIdentityHash","ExpectedWindow","ObservedWindow"}.Order()),
                        "Focus mismatch diagnostic serialized an unexpected field.");
                    return result.Clone();
                }
                var changedProcess=Describe(expectedIdentity,observedIdentity,0x1234,0x5678);
                Require(changedProcess.GetProperty("ExpectedProcessId").GetInt32()==4321&&
                    changedProcess.GetProperty("ObservedProcessId").GetInt32()==8765,"Focus PID was not parsed from the existing identity.");
                Require(changedProcess.GetProperty("ExpectedWindow").GetInt64()==0x1234&&
                    changedProcess.GetProperty("ObservedWindow").GetInt64()==0x5678,"Focus window handles changed.");
                Require(changedProcess.GetProperty("ExpectedIdentityHash").GetString()=="E5B26BE4B3E10F28"&&
                    changedProcess.GetProperty("ObservedIdentityHash").GetString()=="C0BA9F941F472F69","Focus identity hashes were not stable bounded tokens.");
                var sameProcess=Describe(expectedIdentity,"4321:42.65.8",0x1234,0x1234);
                Require(sameProcess.GetProperty("ObservedProcessId").GetInt32()==4321&&
                    sameProcess.GetProperty("ExpectedIdentityHash").GetString()!=sameProcess.GetProperty("ObservedIdentityHash").GetString(),
                    "Different fields in the same process lost their distinct identity tokens.");
                var sameIdentity=Describe(expectedIdentity,expectedIdentity,0x1234,0x1234);
                Require(sameIdentity.GetProperty("ExpectedIdentityHash").GetString()==sameIdentity.GetProperty("ObservedIdentityHash").GetString(),
                    "Identical existing identities produced different tokens.");
                var windowOnly=Describe(expectedIdentity,null,0x1234,0);
                Require(windowOnly.GetProperty("ObservedProcessId").ValueKind==JsonValueKind.Null&&
                    windowOnly.GetProperty("ObservedIdentityHash").ValueKind==JsonValueKind.Null,
                    "A foreground-only mismatch invented focused-field metadata.");
                foreach(var malformed in new[]{"PRIVATE-FOCUS-MISSING-COLON","-1:PRIVATE-FOCUS","2147483648:PRIVATE-FOCUS"," 4321:PRIVATE-FOCUS"})
                {
                    var invalid=Describe(malformed,malformed,0,0);
                    Require(invalid.GetProperty("ExpectedProcessId").ValueKind==JsonValueKind.Null&&
                        invalid.GetProperty("ObservedProcessId").ValueKind==JsonValueKind.Null,"Malformed existing identity invented a process ID.");
                }
                var interrupted=typeof(WindowsTextDelivery).GetNestedType("Interrupted",BindingFlags.NonPublic)!;
                var constructor=interrupted.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Single();
                var stageType=constructor.GetParameters().Single().ParameterType;
                var attach=interrupted.GetMethod("AttachFocusObservation",BindingFlags.Public|BindingFlags.Instance);
                Require(attach!=null,"Opt-in interrupted focus observation is not implemented.");
                foreach(var disabled in new string?[]{null,"0","true"})
                {
                    Environment.SetEnvironmentVariable(Flag,disabled);
                    try
                    {
                        var path=Path.Combine(root,"logs","delivery-metadata.jsonl");var before=File.ReadAllBytes(path);
                        Require(method!.Invoke(null,[expectedIdentity,observedIdentity,(nint)0x1234,(nint)0x5678])==null,
                            "Disabled diagnostics retained focus mismatch metadata.");
                        var error=constructor.Invoke([Enum.Parse(stageType,"FocusedField")]);
                        Require(ReferenceEquals(attach!.Invoke(error,[(nint)0x5678,observedIdentity]),error),"Focus observation replaced the interruption.");
                        Require(interrupted.GetProperty("ObservedForegroundWindow")!.GetValue(error)==null&&
                            interrupted.GetProperty("ObservedIdentity")!.GetValue(error)==null,"Disabled interruption retained raw focus observations.");
                        Sample().WriteIfEnabled();
                        Require(File.ReadAllBytes(path).SequenceEqual(before),"Disabled focus diagnostics changed the journal.");
                    }
                    finally{Environment.SetEnvironmentVariable(Flag,"1");}
                }
                var enabledError=constructor.Invoke([Enum.Parse(stageType,"FocusedField")]);
                attach!.Invoke(enabledError,[(nint)0x5678,observedIdentity]);
                Require((nint?)interrupted.GetProperty("ObservedForegroundWindow")!.GetValue(enabledError)==(nint)0x5678&&
                    (string?)interrupted.GetProperty("ObservedIdentity")!.GetValue(enabledError)==observedIdentity,
                    "Enabled interruption lost its already-read focus observation.");
            });
            Check("Single deleted-character diagnostics expose only offsets and fixed classes",()=>
            {
                var method=typeof(DeliveryDiagnostic).GetMethod("DescribeTextMismatch",BindingFlags.Public|BindingFlags.Static);
                var property=typeof(DeliveryDiagnostic).GetProperty("TextMismatch");
                Require(method!=null&&property!=null,"Single-character mismatch metadata is not implemented.");
                var kindType=Nullable.GetUnderlyingType(property!.PropertyType.GetProperty("SingleDeletedKind")!.PropertyType)!;
                Require(kindType.IsEnum&&Enum.GetNames(kindType).Order().SequenceEqual(new[]{"CR","LF","Space","Nbsp","ZeroWidthSpace","WordJoiner","BOM","ObjectReplacement","Other"}.Order()),"Deleted-character classes accept arbitrary input.");
                JsonElement Describe(string expected,string observed,int boundary)
                {
                    var metadata=method!.Invoke(null,[expected,observed,boundary]);
                    var sample=Sample();property.SetValue(sample,metadata);sample.WriteIfEnabled();
                    var line=File.ReadLines(Path.Combine(root,"logs","delivery-metadata.jsonl")).Last();
                    Require(!line.Contains("PRIVATE-SENTINEL",StringComparison.Ordinal),"Mismatch input text leaked.");
                    using var json=JsonDocument.Parse(line);var result=json.RootElement.GetProperty("Delivery").GetProperty("TextMismatch");
                    Require(result.EnumerateObject().All(p=>new[]{"FirstMismatchOffset","SingleDeletedPosition","SingleDeletedKind","MissingAtOwnedBoundary"}.Contains(p.Name)),"Mismatch diagnostic serialized an unexpected field.");
                    return result.Clone();
                }
                const string prefix="PRIVATE-SENTINEL";
                foreach(var (character,kind) in new[]{('\r',"CR"),('\n',"LF"),(' ',"Space"),('\u00a0',"Nbsp"),('\u200b',"ZeroWidthSpace"),
                    ('\u2060',"WordJoiner"),('\ufeff',"BOM"),('\ufffc',"ObjectReplacement"),('Z',"Other"),('\ud800',"Other")})
                {
                    var metadata=Describe(prefix+character+"tail",prefix+"tail",prefix.Length);
                    Require(metadata.GetProperty("FirstMismatchOffset").GetInt32()==prefix.Length,"Wrong first mismatch offset.");
                    Require(metadata.GetProperty("SingleDeletedPosition").GetInt32()==prefix.Length,"Wrong single deletion position.");
                    Require(metadata.GetProperty("SingleDeletedKind").GetString()==kind,"Wrong fixed deleted-character class.");
                    Require(metadata.GetProperty("MissingAtOwnedBoundary").GetBoolean(),"Missing insertion-boundary diagnosis.");
                }
                foreach(var (expected,observed) in new[]{(prefix,prefix),(prefix+"Ztail",prefix+"Ytail"),
                    (prefix+"ZYtail",prefix+"tail"),(prefix+"Ztail",prefix+"taim"),(prefix,prefix+"Z")})
                {
                    var metadata=Describe(expected,observed,prefix.Length);
                    Require(metadata.GetProperty("SingleDeletedPosition").ValueKind==JsonValueKind.Null&&
                        metadata.GetProperty("SingleDeletedKind").ValueKind==JsonValueKind.Null&&!metadata.GetProperty("MissingAtOwnedBoundary").GetBoolean(),"A different mismatch was classified as one deleted character.");
                }
                var ambiguous=Describe(prefix+"   tail",prefix+"  tail",prefix.Length);
                Require(ambiguous.GetProperty("SingleDeletedPosition").GetInt32()==prefix.Length+2&&ambiguous.GetProperty("MissingAtOwnedBoundary").GetBoolean(),"Equivalent deletion positions lost the insertion-boundary diagnosis.");
                foreach(var boundary in new[]{-1,100000,prefix.Length+1})
                    Require(!Describe(prefix+"Ztail",prefix+"tail",boundary).GetProperty("MissingAtOwnedBoundary").GetBoolean(),"An invalid or different boundary was diagnosed as the missing position.");
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
            Check("Failed text-pattern capture records bounded provider shape without private text",()=>
            {
                var method=typeof(CaptureDiagnostic).GetMethod("FailedProvider");
                Require(method!=null,"Failed-capture provider metadata is not implemented.");
                var queryType=method!.GetParameters().Single().ParameterType;
                var providerType=queryType.GetGenericArguments().Single();
                var provider=Activator.CreateInstance(providerType,["Chrome/invalid",new string('x',120),50004,42,true,true,Enumerable.Range(10000,48).ToArray()])!;
                var factory=typeof(DiagnosticChecks).GetMethod(nameof(ProviderFactory),BindingFlags.Static|BindingFlags.NonPublic)!
                    .MakeGenericMethod(providerType);
                var queries=0;
                Action queried=()=>queries++;
                var query=factory.Invoke(null,[provider,queried])!;
                foreach(var disabled in new string?[]{null,"0","true"})
                {
                    Environment.SetEnvironmentVariable(Flag,disabled);
                    var capture=NewCapture();Stage(capture,"TextPattern");method.Invoke(capture,[query]);
                }
                Require(queries==0,"Disabled diagnostics queried native provider metadata.");
                Environment.SetEnvironmentVariable(Flag,"1");
                var disabledAfterBegin=NewCapture();Stage(disabledAfterBegin,"TextPattern");
                Environment.SetEnvironmentVariable(Flag,null);
                try
                {
                    method.Invoke(disabledAfterBegin,[query]);
                    Require(queries==0,"Disabling diagnostics after capture began still queried metadata.");
                }
                finally{Environment.SetEnvironmentVariable(Flag,"1");}
                var wrongStage=NewCapture();Stage(wrongStage,"Integrity");method.Invoke(wrongStage,[query]);
                Require(queries==0,"Provider metadata queried before text-pattern refusal.");
                var failed=NewCapture();Stage(failed,"TextPattern");method.Invoke(failed,[query]);
                Require(queries==1,"Failed capture did not query one provider shape.");
                var line=File.ReadLines(Path.Combine(root,"logs","capture-metadata.jsonl")).Last();
                using var json=JsonDocument.Parse(line);var item=json.RootElement;
                Require(item.GetProperty("Stage").GetString()=="failed-provider","Missing failed-provider event.");
                var shape=item.GetProperty("Provider");
                Require(shape.EnumerateObject().All(p=>new[]{"FrameworkId","ClassName","ControlTypeId","TargetProcessId","HasKeyboardFocus","IsKeyboardFocusable","SupportedPatternIds"}.Contains(p.Name)),"Unexpected provider data field.");
                Require(shape.GetProperty("FrameworkId").GetString()=="Chromeinvalid","Framework metadata was not sanitized.");
                Require(shape.GetProperty("ClassName").GetString()!.Length==80,"Provider class metadata was not bounded.");
                Require(shape.GetProperty("ControlTypeId").GetInt32()==50004&&shape.GetProperty("TargetProcessId").GetInt32()==42,"Wrong provider technical identity.");
                Require(shape.GetProperty("HasKeyboardFocus").GetBoolean()&&shape.GetProperty("IsKeyboardFocusable").GetBoolean(),"Missing focus booleans.");
                Require(shape.GetProperty("SupportedPatternIds").GetArrayLength()==32,"Supported patterns were not bounded.");
                Require(!line.Contains("PRIVATE-SENTINEL",StringComparison.Ordinal),"Private content leaked.");
                var original=new InvalidOperationException("PRIVATE-SENTINEL");
                Action failQuery=()=>throw original;
                var failedQuery=factory.Invoke(null,[provider,failQuery])!;
                method.Invoke(failed,[failedQuery]);
                var last=File.ReadLines(Path.Combine(root,"logs","capture-metadata.jsonl")).Last();
                Require(last==line,"Provider failure changed the existing diagnostic evidence.");
            });
            Check("Capture window scope proof records only fixed status and numeric handles",()=>
            {
                var captureType=typeof(CaptureDiagnostic);
                var method=captureType.GetMethod("Scope",BindingFlags.NonPublic|BindingFlags.Instance);
                Require(method!=null,"Capture window scope proof is not implemented.");
                var scopeType=typeof(DeliveryDiagnostic).Assembly.GetType("LocalDictation.App.Services.AutomationWindowScope+Scope")!;
                var statusType=typeof(DeliveryDiagnostic).Assembly.GetType("LocalDictation.App.Services.AutomationWindowScope+ScopeStatus")!;
                object Scope(string status,nint host,nint nativeRoot)=>Activator.CreateInstance(scopeType,
                    BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,[Enum.Parse(statusType,status),host,nativeRoot],null)!;
                var proofRoot=Path.Combine(root,"scope-proof");
                Environment.SetEnvironmentVariable(Home,proofRoot);
                try
                {
                    var sample=Scope("Matched",(nint)0x2345,(nint)0x1234);
                    foreach(var disabled in new string?[]{null,"0","true"})
                    {
                        Environment.SetEnvironmentVariable(Flag,disabled);
                        var optedOut=NewCapture();method!.Invoke(optedOut,[(nint)0x1234,sample]);
                        Require(!Directory.Exists(proofRoot),"Disabled scope proof created diagnostic files.");
                        Environment.SetEnvironmentVariable(Flag,"1");method.Invoke(optedOut,[(nint)0x1234,sample]);
                        Require(!Directory.Exists(proofRoot),"A capture begun opted out later retained scope proof.");
                    }
                    var enabled=NewCapture();Environment.SetEnvironmentVariable(Flag,"0");
                    method!.Invoke(enabled,[(nint)0x1234,sample]);
                    Require(!Directory.Exists(proofRoot),"Scope proof did not recheck current opt-out.");
                    Environment.SetEnvironmentVariable(Flag,"1");
                    foreach(var (status,encoded,matched,host,nativeRoot) in new[]{
                        ("Matched","matched",true,0x2345L,0x123456789L),
                        ("Mismatched","mismatched",false,0x2345L,0x9876L),
                        ("Unresolved","unresolved",false,0L,0L)})
                    {
                        method.Invoke(enabled,[new IntPtr(0x123456789L),Scope(status,(nint)host,(nint)nativeRoot)]);
                        var line=File.ReadLines(Path.Combine(proofRoot,"logs","capture-metadata.jsonl")).Last();
                        using var json=JsonDocument.Parse(line);var item=json.RootElement;
                        Require(item.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(new[]{
                            "Time","ProcessId","Stage","Milliseconds","Status","ExpectedWindow","NativeHost","Root","Matched"}.Order()),
                            "Scope proof serialized an unexpected data field.");
                        Require(item.GetProperty("Stage").GetString()=="window-scope-proof"&&
                            item.GetProperty("Status").GetString()==encoded&&item.GetProperty("Matched").GetBoolean()==matched,
                            "Scope proof changed its fixed stage or enum-derived status.");
                        Require(item.GetProperty("ExpectedWindow").GetInt64()==0x123456789L&&
                            item.GetProperty("NativeHost").GetInt64()==host&&item.GetProperty("Root").GetInt64()==nativeRoot,
                            "Scope proof changed its already-resolved numeric handles.");
                        Require(item.GetProperty("ProcessId").GetInt32()==Environment.ProcessId&&
                            item.GetProperty("Time").GetDateTimeOffset()<=DateTimeOffset.UtcNow&&item.GetProperty("Milliseconds").GetDouble()>=0,
                            "Scope proof has invalid process/time metadata.");
                        Require(!line.Contains("PRIVATE-SENTINEL",StringComparison.Ordinal),"Private content leaked into scope proof.");
                    }
                }
                finally
                {Environment.SetEnvironmentVariable(Home,root);Environment.SetEnvironmentVariable(Flag,"1");}
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
    static Func<T> ProviderFactory<T>(T provider,Action queried)=>()=>{queried();return provider;};
    static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
}
