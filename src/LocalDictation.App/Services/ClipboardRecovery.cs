namespace LocalDictation.App.Services;

public sealed record ClipboardRecoveryResult(bool Succeeded,string Message);

/// <summary>Retains formats only when automatic clipboard restoration needs a retry.</summary>
public static class ClipboardRecovery
{
    static readonly object gate=new();
    static ClipboardTransaction? active;
    public static event Action? Changed;
    // Reserve before replacement so another attempt or Exit cannot discard a
    // backup still being captured, returned, or retried.
    public static bool HasUnsettledTransaction
    {
        get{_=HasPending;lock(gate)return active!=null;}
    }
    public static bool HasPending
    {
        get
        {
            ClipboardTransaction? release=null;
            lock(gate)
            {
                if(active is {RecoveryPending:true} lease&&lease.WasReplacedByAnotherOwner)release=lease;
            }
            // Keep the registry occupied until Complete finishes under the lease
            // lock. A concurrent deadline must not restore over a newer attempt.
            if(release!=null)release.ReleaseSuperseded();
            lock(gate)return active?.RecoveryPending==true;
        }
    }
    public static string Message
    {
        get
        {
            if(!HasPending)return "";
            lock(gate)return active?.RecoveryMessage??"";
        }
    }
    static void Notify()
    {
        if(Changed is not {} changed)return;
        foreach(Action listener in changed.GetInvocationList())
            try{listener();}catch(Exception error){Diagnostics.Error("clipboard recovery notification",error);}
    }
    internal static void Reserve(ClipboardTransaction lease)
    {
        _=HasPending;
        lock(gate)
        {
            if(active!=null)throw new InvalidOperationException(active.RecoveryPending?active.RecoveryMessage:"Another clipboard delivery is still active.");
            active=lease;
        }
    }
    public static void Hold(ClipboardTransaction lease)
    {
        bool notify;
        lock(gate)
        {
            if(active!=null&&!ReferenceEquals(active,lease))throw new InvalidOperationException("Another previous clipboard backup is already retained.");
            notify=!ReferenceEquals(active,lease)||!lease.RecoveryPending;
            active=lease;lease.RecoveryPending=true;
        }
        if(notify)Notify();
    }
    internal static void Release(ClipboardTransaction lease)
    {
        bool notify;
        lock(gate){notify=ReferenceEquals(active,lease);if(notify)active=null;}
        if(notify)Notify();
    }
    internal static bool IsCurrentHeldLease(ClipboardTransaction lease)
    {
        lock(gate)return ReferenceEquals(active,lease)&&lease.RecoveryPending;
    }
    /// <summary>Retries failed restoration independently of insertion acknowledgement.</summary>
    public static bool TryRestorePending()
    {
        _=HasPending;
        ClipboardTransaction? lease;lock(gate)lease=active;
        return lease is null||IsCurrentHeldLease(lease)&&lease.TryRestorePending();
    }
    public static ClipboardRecoveryResult RestoreAfterInspection()
    {
        if(!HasPending)return new(true,"No previous clipboard needs recovery.");
        ClipboardTransaction? lease;lock(gate)lease=active;
        if(lease==null)return new(true,"No previous clipboard needs recovery.");
        // The user explicitly requests this after checking/stopping the destination.
        if(lease.RestoreExplicitly())return new(true,lease.WasSuperseded?"A newer clipboard copy was preserved.":"The previous clipboard was restored.");
        return new(false,lease.RecoveryMessage);
    }
}
