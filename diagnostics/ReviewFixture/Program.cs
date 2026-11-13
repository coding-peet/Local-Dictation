using LocalDictation.Core;

// Storage fixture only. No app assembly, native input, clipboard, microphone or network.
if(args.Length!=2||args[0]!="--home")
{
    Console.Error.WriteLine("Usage: ReviewFixture --home C:\\AI\\build\\<isolated-test>\\home");
    return 2;
}
var home=Path.GetFullPath(args[1]);
if(!home.StartsWith(@"C:\AI\build\",StringComparison.OrdinalIgnoreCase)||
    home.TrimEnd('\\').Length<=@"C:\AI\build".Length)
    throw new ArgumentException("Only a new isolated directory under C:\\AI\\build is allowed.");
if(Directory.Exists(home)&&Directory.EnumerateFileSystemEntries(home).Any())
    throw new InvalidOperationException("The fixture refuses to replace an existing profile.");
var data=Path.Combine(home,"data");
new Settings{MicrophoneId="",VerifyAfterRecording=false,Live=false,InsertAtCursor=false,Theme="Dark"}.Save(data);
var store=new ReviewStore(Path.Combine(data,"review"));
var payloads=new[]{"Kontrolná veta jeden.","Kontrolná veta dva."};
for(var i=0;i<payloads.Length;i++)
{
    var document=new ReviewDocument
    {
        Created=DateTimeOffset.Now.AddSeconds(-i),
        PrimaryModel="kinit/whisper-large-v3-turbo-sk",
        Reason="Synthetic native insertion check "+(i+1)+" of 2."
    };
    document.SetPrimary(payloads[i]);
    store.Save(document);
    Console.WriteLine($"Created synthetic Review {i+1}: {document.Id}");
}
Console.WriteLine("Created isolated profile: "+home);
return 0;
