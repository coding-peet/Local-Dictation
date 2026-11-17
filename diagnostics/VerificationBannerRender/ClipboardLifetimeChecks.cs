using System.Reflection;
using System.Runtime.CompilerServices;
using LocalDictation.App.Services;

static class ClipboardLifetimeChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;

    // An empty synthetic lease exercises the real registry/Dispose lifecycle
    // without opening, reading or changing the Windows clipboard.
    internal static ClipboardTransaction Lease()
    {
        var lease=(ClipboardTransaction)RuntimeHelpers.GetUninitializedObject(typeof(ClipboardTransaction));
        typeof(ClipboardTransaction).GetField("stateGate",Fields)!.SetValue(lease,new object());
        var saved=typeof(ClipboardTransaction).GetField("saved",Fields)!;
        saved.SetValue(lease,Activator.CreateInstance(saved.FieldType));
        typeof(ClipboardRecovery).GetMethod("Reserve",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[lease]);
        return lease;
    }

    public static void Run(List<string> checks)
    {
        foreach(var acknowledged in new[]{false,true})
        {
            var lease=Lease();
            try
            {
                lease.MarkMayHaveQueued();
                if(acknowledged)lease.Acknowledge();
                lease.Dispose();
                if(ClipboardRecovery.HasPending||ClipboardRecovery.HasUnsettledTransaction)
                    throw new InvalidOperationException("A completed attempt retained the clipboard solely because insertion was unconfirmed.");
                using var next=Lease();
                next.Dispose();
                if(ClipboardRecovery.HasUnsettledTransaction)
                    throw new InvalidOperationException("The next attempt could not release its independent clipboard lease.");
                checks.Add("clipboard lease ends after "+(acknowledged?"confirmed":"unconfirmed")+" attempt");
            }
            finally
            {
                // Old-code RED cleanup is also entirely memory-only.
                if(ClipboardRecovery.HasPending)ClipboardRecovery.RestoreAfterInspection();
            }
        }
        {
            var lease=Lease();
            ClipboardRecovery.Hold(lease);
            try
            {
                if(!ClipboardRecovery.TryRestorePending()||ClipboardRecovery.HasUnsettledTransaction)
                    throw new InvalidOperationException("Automatic restoration still requires insertion acknowledgement.");
                checks.Add("automatic restoration retry does not require insertion confirmation");
            }
            finally{if(ClipboardRecovery.HasPending)ClipboardRecovery.RestoreAfterInspection();}
        }
        {
            var lease=Lease();
            try
            {
                lease.MarkMayHaveQueued();
                // Simulate a blocked destination readback: Apply cannot reach its
                // finally yet, but the clipboard must still return automatically.
                var deadline=System.Diagnostics.Stopwatch.StartNew();
                while(ClipboardRecovery.HasUnsettledTransaction&&deadline.Elapsed<TimeSpan.FromSeconds(3))Thread.Sleep(10);
                if(ClipboardRecovery.HasUnsettledTransaction)
                    throw new InvalidOperationException("A stalled readback kept the clipboard beyond the attempt deadline.");
                checks.Add("clipboard deadline releases independently of stalled readback");
            }
            finally{lease.Dispose();}
        }
        {
            var returned=Lease();returned.MarkMayHaveQueued();returned.Dispose();
            var delivery=(WindowsTextDelivery)RuntimeHelpers.GetUninitializedObject(typeof(WindowsTextDelivery));
            typeof(WindowsTextDelivery).GetField("lifecycleGate",Fields)!.SetValue(delivery,new object());
            var pending=typeof(WindowsTextDelivery).GetNestedType("PendingNativePaste",BindingFlags.NonPublic)!;
            var snapshot=new LocalDictation.Core.PasteAcknowledgementSnapshot("field","fixture","",0,"","draft","draft",true,null);
            var diagnostic=RuntimeHelpers.GetUninitializedObject(typeof(DeliveryDiagnostic));
            var provider=typeof(WindowsTextDelivery).GetNestedType("ProviderIdentity",BindingFlags.NonPublic)!;
            var shape=Activator.CreateInstance(provider,Fields,null,["fixture","fixture",50004],null)!;
            var receipt=Activator.CreateInstance(pending,Fields,null,
                [returned,snapshot,shape,(nint)42,0,0L,System.Diagnostics.Stopwatch.GetTimestamp(),diagnostic],null);
            typeof(WindowsTextDelivery).GetField("pendingPaste",Fields)!.SetValue(delivery,receipt);
            if(!delivery.HasPendingAcknowledgement||ClipboardRecovery.HasUnsettledTransaction)
                throw new InvalidOperationException("Late insertion readback still depends on retaining the clipboard.");
            delivery.Suspend();
            if(delivery.HasPendingAcknowledgement)
                throw new InvalidOperationException("Intervening input failed to cancel a late insertion readback.");
            checks.Add("late readback survives clipboard return and cancels on input");
        }
        {
            var lease=Lease();var sends=0;
            try
            {
                var arm=typeof(ClipboardTransaction).GetMethod("ArmReturnDeadline",Fields);
                if(arm==null)throw new InvalidOperationException("Clipboard replacement has no deadline before pre-dispatch UIA checks.");
                arm.Invoke(lease,null);
                var deadline=System.Diagnostics.Stopwatch.StartNew();
                while(ClipboardRecovery.HasUnsettledTransaction&&deadline.Elapsed<TimeSpan.FromSeconds(3))Thread.Sleep(10);
                if(ClipboardRecovery.HasUnsettledTransaction)
                    throw new InvalidOperationException("Pre-dispatch UIA checks kept the clipboard indefinitely.");
                var dispatch=typeof(ClipboardTransaction).GetMethod("Dispatch",Fields);
                if(dispatch==null)throw new InvalidOperationException("Native dispatch cannot reject an expired clipboard lease.");
                try{dispatch.Invoke(lease,[(Action)(()=>sends++)]);throw new InvalidOperationException("An expired lease permitted native dispatch.");}
                catch(TargetInvocationException error) when(error.InnerException is InvalidOperationException){}
                if(sends!=0)throw new InvalidOperationException("Input was sent after clipboard return.");
                checks.Add("pre-dispatch deadline returns clipboard and prevents expired input");
            }
            finally{lease.Dispose();}
        }
        {
            var lease=Lease();
            try
            {
                var arm=typeof(ClipboardTransaction).GetMethod("ArmReturnDeadline",Fields)!;
                arm.Invoke(lease,null);
                var versionField=typeof(ClipboardTransaction).GetField("returnDeadlineVersion",Fields);
                if(versionField==null)throw new InvalidOperationException("A stale pre-dispatch timer can return the clipboard during a renewed dispatch window.");
                var oldVersion=(long)versionField.GetValue(lease)!;
                arm.Invoke(lease,null);
                typeof(ClipboardTransaction).GetMethod("ReturnAtDeadline",Fields)!.Invoke(lease,[oldVersion]);
                if(!ClipboardRecovery.HasUnsettledTransaction)
                    throw new InvalidOperationException("A stale deadline ended the renewed paste consumption window.");
                checks.Add("renewed dispatch window ignores a queued stale deadline");
            }
            finally{lease.Dispose();}
        }
        foreach(var restorationPending in new[]{false,true})
        {
            var lease=Lease();var sends=0;
            try
            {
                typeof(ClipboardTransaction).GetMethod("ArmReturnDeadline",Fields)!.Invoke(lease,null);
                // Model a delayed timer callback without touching the real
                // clipboard. A retained backup after failed restoration is also
                // unfinished, so finished alone cannot authorize dispatch.
                ((Timer)typeof(ClipboardTransaction).GetField("returnDeadline",Fields)!.GetValue(lease)!).Change(Timeout.Infinite,Timeout.Infinite);
                if(restorationPending)ClipboardRecovery.Hold(lease);
                var started=typeof(ClipboardTransaction).GetField("returnDeadlineStartedAt",Fields);
                if(started==null)throw new InvalidOperationException("An unfinished paste lease has no monotonic dispatch deadline when the return callback is delayed or restoration fails.");
                started.SetValue(lease,System.Diagnostics.Stopwatch.GetTimestamp()-2*System.Diagnostics.Stopwatch.Frequency);
                var version=(long)typeof(ClipboardTransaction).GetField("returnDeadlineVersion",Fields)!.GetValue(lease)!;
                try{lease.Dispatch(()=>sends++);throw new InvalidOperationException("An elapsed pre-dispatch window permitted native dispatch.");}
                catch(InvalidOperationException error) when(error.Message.Contains("lease has ended",StringComparison.Ordinal)){}
                if(sends!=0||(long)typeof(ClipboardTransaction).GetField("returnDeadlineVersion",Fields)!.GetValue(lease)! !=version)
                    throw new InvalidOperationException("An expired unfinished attempt sent input or renewed its consumption window.");
                // Expiry remains terminal even while the backup is retained;
                // another path cannot restart that same attempt's window.
                typeof(ClipboardTransaction).GetMethod("ArmReturnDeadline",Fields)!.Invoke(lease,null);
                if((long)typeof(ClipboardTransaction).GetField("returnDeadlineVersion",Fields)!.GetValue(lease)! !=version)
                    throw new InvalidOperationException("An expired attempt restarted its clipboard deadline.");
                checks.Add("expired dispatch refuses "+(restorationPending?"unfinished restoration":"delayed return callback"));
            }
            finally{lease.Dispose();}
        }
    }
}
