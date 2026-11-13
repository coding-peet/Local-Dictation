using System.Diagnostics;
using LocalDictation.App.Services;
using LocalDictation.Core;
using Xunit;

public class AutomationWorkerRecoveryTests
{
    // Provider exit precedes task publication. Probe only fresh read-only work.
    static async Task<int> FreshWorker(AutomationWorker worker)
    {
        var elapsed=Stopwatch.StartNew();
        while(true)
        {
            try{return await worker.Run(_=>Environment.CurrentManagedThreadId);}
            catch(InvalidOperationException error) when(error.Message.Contains("provider is unavailable",StringComparison.Ordinal))
            {
                Assert.True(elapsed.Elapsed<TimeSpan.FromSeconds(3),"Completed provider permanently blocked fresh live work.");
                await Task.Delay(5);
            }
        }
    }

    [Fact]
    public async Task AFinishedSlowReadbackMustNotBlockTheNextDraftAfterAPause()
    {
        using var worker=new AutomationWorker();
        using var entered=new ManualResetEventSlim();
        using var release=new ManualResetEventSlim();
        var exited=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard=new TargetGuard("original-field","prefix suffix",7);
        const string first="Prvá veta. ";
        var field=guard.ReplacedText(first);
        guard.Applied(first,field,7+first.Length);
        var calls=0;var workerThread=0;
        var slow=worker.Run(_=>
        {
            workerThread=Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref calls);entered.Set();
            try
            {
                if(!release.Wait(TimeSpan.FromSeconds(8)))throw new TimeoutException("Test barrier was not released.");
                return field;
            }
            finally{exited.TrySetResult();}
        },TimeSpan.FromSeconds(1));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(async()=>await slow);
            var premature=false;
            await Assert.ThrowsAsync<InvalidOperationException>(async()=>await worker.Run(_=>premature=true));
            Assert.False(premature);
            release.Set();await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(workerThread,await FreshWorker(worker));
            var resumed=await worker.Run(_=>
            {
                Assert.True(guard.CanResume("original-field",field));
                return guard.ReplacedText("Opravená prvá veta. Pokračujem po pauze.");
            });
            Assert.Equal("prefix Opravená prvá veta. Pokračujem po pauze.suffix",resumed);
            Assert.Equal(1,calls);
            Assert.Equal(ApartmentState.MTA,await worker.Run(_=>Thread.CurrentThread.GetApartmentState()));
        }
        finally{release.Set();await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));}
    }

    [Fact]
    public async Task ExpiredQueuedReadbackMustBeSkippedWithoutPermanentlyBlockingLaterUpdates()
    {
        using var worker=new AutomationWorker();
        using var entered=new ManualResetEventSlim();
        using var release=new ManualResetEventSlim();
        var first=worker.Run(_=>
        {
            entered.Set();
            if(!release.Wait(TimeSpan.FromSeconds(8)))throw new TimeoutException("Test barrier was not released.");
            return Environment.CurrentManagedThreadId;
        },TimeSpan.FromSeconds(6));
        var skippedCalls=0;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var skipped=worker.Run(_=>Interlocked.Increment(ref skippedCalls),TimeSpan.FromSeconds(1));
            await Assert.ThrowsAsync<InvalidOperationException>(async()=>await skipped);
            var premature=false;
            await Assert.ThrowsAsync<InvalidOperationException>(async()=>await worker.Run(_=>premature=true));
            Assert.False(premature);
            release.Set();
            var thread=await first;
            Assert.Equal(thread,await FreshWorker(worker));
            Assert.Equal(0,skippedCalls);
        }
        finally{release.Set();try{await first;}catch(InvalidOperationException){}}
    }

    [Fact]
    public async Task RepeatedRecoverableTimeoutsReuseTheOriginalWorkerWithoutReplay()
    {
        using var worker=new AutomationWorker();
        var thread=await worker.Run(_=>Environment.CurrentManagedThreadId);
        var calls=0;
        for(var i=0;i<2;i++)
        {
            using var entered=new ManualResetEventSlim();
            using var release=new ManualResetEventSlim();
            var exited=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var slow=worker.Run(_=>
            {
                Interlocked.Increment(ref calls);entered.Set();
                try
                {
                    if(!release.Wait(TimeSpan.FromSeconds(8)))throw new TimeoutException("Test barrier was not released.");
                    return 1;
                }
                finally{exited.TrySetResult();}
            },TimeSpan.FromSeconds(1));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
                await Assert.ThrowsAsync<InvalidOperationException>(async()=>await slow);
                release.Set();await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(thread,await FreshWorker(worker));
            }
            finally{release.Set();await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));}
        }
        Assert.Equal(2,calls);
    }

    [Fact]
    public async Task CancelledActiveReadbackCannotContinueToDeliveryWhenTheProviderReturns()
    {
        using var worker=new AutomationWorker();
        using var entered=new ManualResetEventSlim();
        using var release=new ManualResetEventSlim();
        var exited=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveryCalls=0;
        var slow=worker.Run(token=>
        {
            entered.Set();
            try
            {
                if(!release.Wait(TimeSpan.FromSeconds(8)))throw new TimeoutException("Test barrier was not released.");
                token.ThrowIfCancellationRequested();
                return Interlocked.Increment(ref deliveryCalls);
            }
            finally{exited.TrySetResult();}
        },TimeSpan.FromSeconds(1));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(async()=>await slow);
            release.Set();await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await FreshWorker(worker);
            Assert.Equal(0,deliveryCalls);
        }
        finally{release.Set();await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));}
    }
}
