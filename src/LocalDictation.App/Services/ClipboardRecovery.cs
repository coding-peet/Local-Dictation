namespace LocalDictation.App.Services;

public sealed record ClipboardRecoveryResult(bool Succeeded,string Message);

/// <summary>Retains previous clipboard formats when an uncertain paste or failed restoration needs deliberate recovery.</summary>
public static class ClipboardRecovery
{
    static readonly object gate=new();
    static ClipboardTransaction? active;
    public static event Action? Changed;
    // A third-party UIA call can outlive its worker deadline. Reservation lifetime
    // must therefore block Exit even before Apply reaches Dispose/Hold.
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
                if(active is {RecoveryPending:true} lease&&lease.WasReplacedByAnotherOwner){release=lease;active=null;}
            }
            if(release!=null){release.ReleaseSuperseded();Notify();}
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
        lock(gate)
        {
            if(active!=null&&!ReferenceEquals(active,lease))throw new InvalidOperationException("Another previous clipboard backup is already retained.");
            active=lease;lease.RecoveryPending=true;
        }
        Notify();
    }
    internal static void Release(ClipboardTransaction lease)
    {
        bool notify;
        lock(gate){notify=ReferenceEquals(active,lease);if(notify)active=null;}
        if(notify)Notify();
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
