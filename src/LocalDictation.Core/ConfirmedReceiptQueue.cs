namespace LocalDictation.Core;

/// <summary>Process-memory facts awaiting persistence; unrelated to a caller's wait or target lifecycle.</summary>
public sealed class ConfirmedReceiptQueue<T> where T:class
{
    readonly object gate=new();
    readonly Queue<T> receipts=new();
    public T? Current{get{lock(gate)return receipts.TryPeek(out var receipt)?receipt:null;}}
    public void Publish(T receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock(gate)receipts.Enqueue(receipt);
    }
    public void ClearSame(T receipt)
    {
        lock(gate)
            if(receipts.TryPeek(out var current)&&ReferenceEquals(current,receipt))receipts.Dequeue();
    }
}
