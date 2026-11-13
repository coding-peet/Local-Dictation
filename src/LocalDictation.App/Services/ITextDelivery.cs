using LocalDictation.Core;

namespace LocalDictation.App.Services;

public interface ITextDelivery
{
    nint Window {get;}
    bool IsConflict {get;}
    string Reason {get;}
    string InsertedText {get;}
    bool HasTransportFailure {get;}
    bool IsOriginalFieldCurrent(CancellationToken token);
    void Invalidate();
    void Suspend();
    bool OwnsProgrammaticFocus(nint root);
    bool Apply(string next,CancellationToken token,bool restoreFocus=false);
    bool Apply(string next,CancellationToken token,DeliveryIntent intent);
}
