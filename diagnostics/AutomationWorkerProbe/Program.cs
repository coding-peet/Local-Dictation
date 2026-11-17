using System.IO;
using System.Text.Json;
using LocalDictation.App.Services;

namespace LocalDictation.AutomationWorkerProbe;

static class Program
{
    sealed record Result(string Name,bool Passed,string? Failure=null);
    static void Require(bool condition,string failure)
    {
        if(!condition)throw new InvalidOperationException(failure);
    }
    static async Task<InvalidOperationException> Refusal<T>(Task<T> operation)
    {
        try{await operation;}
        catch(InvalidOperationException error){return error;}
        throw new InvalidOperationException("The worker accepted an operation that should fail closed.");
    }
    static async Task Main(string[] args)
    {
        if(args.Length!=2||args[0]!="--report")
        {Console.Error.WriteLine("Usage: AutomationWorkerProbe --report ABSOLUTE_PATH");Environment.ExitCode=2;return;}
        var report=Path.GetFullPath(args[1]);
        var started=DateTimeOffset.UtcNow;
        var results=new List<Result>();
        async Task Check(string name,Func<Task> check)
        {
            try{await check();results.Add(new(name,true));}
            catch(Exception error){results.Add(new(name,false,error.Message));Environment.ExitCode=1;}
        }
        await Check("UI Automation runs on a dedicated background MTA thread",async()=>
        {
            var caller=Environment.CurrentManagedThreadId;
            using var worker=new AutomationWorker();
            var observed=await worker.Run(_=>new
            {
                Id=Environment.CurrentManagedThreadId,
                Apartment=Thread.CurrentThread.GetApartmentState(),
                Thread.CurrentThread.IsBackground
            });
            Require(observed.Id!=caller,"The operation ran on the caller thread.");
            Require(observed.IsBackground,"The worker can keep the client alive after shutdown.");
            Require(observed.Apartment==ApartmentState.MTA,$"UI Automation requires MTA; observed {observed.Apartment}.");
        });
        await Check("Queued operations remain serialized on the same worker",async()=>
        {
            using var worker=new AutomationWorker();
            using var entered=new ManualResetEventSlim();
            using var release=new ManualResetEventSlim();
            var order=new List<int>();var threads=new HashSet<int>();var active=0;var concurrent=false;
            int Work(int value,bool block)
            {
                if(Interlocked.Increment(ref active)!=1)concurrent=true;
                try
                {
                    if(block){entered.Set();Require(release.Wait(TimeSpan.FromSeconds(3)),"Queue test was not released.");}
                    order.Add(value);threads.Add(Environment.CurrentManagedThreadId);return value;
                }
                finally{Interlocked.Decrement(ref active);}
            }
            var first=worker.Run(_=>Work(1,true),TimeSpan.FromSeconds(4));
            try
            {
                Require(entered.Wait(TimeSpan.FromSeconds(2)),"The worker did not begin its first operation.");
                var second=worker.Run(_=>Work(2,false),TimeSpan.FromSeconds(4));
                var third=worker.Run(_=>Work(3,false),TimeSpan.FromSeconds(4));
                release.Set();await Task.WhenAll(first,second,third);
                Require(!concurrent,"Native operations overlapped.");
                Require(threads.Count==1,"The worker changed threads between calls.");
                Require(order.SequenceEqual(new[]{1,2,3}),"Queued native operations changed order.");
            }
            finally{release.Set();await first;}
        });
        await Check("Provider exceptions retain their identity without poisoning the worker",async()=>
        {
            using var worker=new AutomationWorker();
            var original=new ArgumentException("Synthetic provider failure");
            Exception? observed=null;
            try{await worker.Run<int>(_=>throw original);}
            catch(Exception error){observed=error;}
            Require(ReferenceEquals(original,observed),"The worker replaced the provider exception.");
            Require(await worker.Run(_=>42)==42,"An ordinary provider failure poisoned later operations.");
        });
        await Check("A noncooperative provider deadline refuses later work without waiting for it",async()=>
        {
            using var worker=new AutomationWorker();
            using var entered=new ManualResetEventSlim();
            using var release=new ManualResetEventSlim();
            var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var timed=worker.Run(_=>
            {
                entered.Set();
                try{Require(release.Wait(TimeSpan.FromSeconds(3)),"Synthetic blocked provider was not released.");return 1;}
                finally{finished.TrySetResult();}
            },TimeSpan.FromMilliseconds(150));
            try
            {
                Require(entered.Wait(TimeSpan.FromSeconds(2)),"The blocked provider did not begin.");
                var failure=await Refusal(timed);
                Require(failure.Message.Contains("did not answer",StringComparison.Ordinal),"The timeout lost its diagnostic refusal.");
                var invoked=false;
                var rejected=await Refusal(worker.Run(_=>{invoked=true;return 2;}));
                Require(!invoked&&rejected.Message.Contains("unavailable",StringComparison.Ordinal),"A timed-out worker accepted a new native operation.");
            }
            finally{release.Set();await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));}
        });
        await Check("The operation token is cancelled when its deadline expires",async()=>
        {
            using var worker=new AutomationWorker();
            var tokenCancelled=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation=worker.Run(token=>
            {
                token.WaitHandle.WaitOne();
                tokenCancelled.TrySetResult(token.IsCancellationRequested);
                token.ThrowIfCancellationRequested();return 1;
            },TimeSpan.FromMilliseconds(150));
            await Refusal(operation);
            Require(await tokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2)),"The provider did not observe cancellation.");
        });
        await Check("A queued operation cancelled before execution never calls its provider",async()=>
        {
            using var worker=new AutomationWorker();
            using var entered=new ManualResetEventSlim();
            using var release=new ManualResetEventSlim();
            var first=worker.Run(_=>
            {
                entered.Set();Require(release.Wait(TimeSpan.FromSeconds(3)),"Queued cancellation test was not released.");return 1;
            },TimeSpan.FromSeconds(4));
            try
            {
                Require(entered.Wait(TimeSpan.FromSeconds(2)),"The queue barrier did not begin.");
                var invoked=false;
                var cancelled=worker.Run(_=>{invoked=true;return 2;},TimeSpan.FromMilliseconds(150));
                // This accepted operation is a barrier after the cancelled queue entry.
                var after=worker.Run(_=>3,TimeSpan.FromSeconds(4));
                await Refusal(cancelled);release.Set();
                Require(await after==3,"The queued barrier did not complete.");
                await first;Require(!invoked,"A cancelled queued native operation still ran.");
            }
            finally{release.Set();await first;}
        });
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var output=new
        {
            Fixture="Local Dictation AutomationWorkerProbe",
            StartedUtc=started,CompletedUtc=DateTimeOffset.UtcNow,
            NoUiAutomationQueries=true,NoWindowsCreated=true,NoClipboardAccess=true,
            NoMicrophoneCapture=true,NoApiConnection=true,
            Passed=results.All(r=>r.Passed),Results=results
        };
        File.WriteAllText(report,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var result in results)Console.WriteLine($"{(result.Passed?"PASS":"FAIL")} {result.Name}{(result.Failure is null?"":" — "+result.Failure)}");
    }
}
