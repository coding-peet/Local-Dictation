namespace LocalDictation.Core;

/// <summary>A finish-time destination decision cannot be reversed by a later return.</summary>
public sealed class CompletionDestinationGate
{
    bool confirmed, originalField, interrupted;
    volatile bool finishing;
    public bool IsFinishing => finishing;
    public bool CanBeginLiveDelivery => !finishing;
    public bool CanInsertAutomatically => IsFinishing && confirmed && originalField && !interrupted;
    public void BeginFinish() => finishing = true;
    public void ConfirmOriginalField(bool current)
    {
        if (!IsFinishing || confirmed) return;
        originalField = current;
        confirmed = true;
    }
    public void Interrupt() { if (IsFinishing) interrupted = true; }
}
