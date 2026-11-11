namespace LocalDictation.App.Services;

public interface ITextDelivery
{
    nint Window {get;}
    bool IsConflict {get;}
    string Reason {get;}
    string InsertedText {get;}
    void Invalidate();
    void Suspend();
    bool OwnsProgrammaticFocus(nint root);
    bool Apply(string next,CancellationToken token,bool restoreFocus=false);
}
