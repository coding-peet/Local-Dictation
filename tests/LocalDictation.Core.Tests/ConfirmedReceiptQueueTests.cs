using LocalDictation.Core;
using Xunit;

public class ConfirmedReceiptQueueTests
{
    sealed record Receipt(string Text,bool IsFinal);

    [Fact]
    public void PublishedFactRemainsObservableWhenTheCallingWaitLosesItsReturn()
    {
        var receipts=new ConfirmedReceiptQueue<Receipt>();
        var receipt=new Receipt("already inserted",true);
        void CompleteThenLoseWait()
        {
            receipts.Publish(receipt);
            throw new TimeoutException("The caller stopped waiting after acknowledgement.");
        }
        Assert.Throws<TimeoutException>(CompleteThenLoseWait);
        Assert.Same(receipt,receipts.Current);
    }

    [Fact]
    public void EqualTextInAnotherReceiptCannotConsumeThePublishedInstance()
    {
        var receipts=new ConfirmedReceiptQueue<Receipt>();
        var receipt=new Receipt("already inserted",true);
        receipts.Publish(receipt);
        receipts.ClearSame(new Receipt("already inserted",true));
        Assert.Same(receipt,receipts.Current);
    }

    [Fact]
    public void ConsumingAnOlderFactCannotEraseTheNextOrLaterPublishedFact()
    {
        var receipts=new ConfirmedReceiptQueue<Receipt>();
        var first=new Receipt("first",false);
        var second=new Receipt("second",true);
        receipts.Publish(first);receipts.Publish(second);
        Assert.Same(first,receipts.Current);
        receipts.ClearSame(first);
        Assert.Same(second,receipts.Current);
        receipts.ClearSame(first);
        Assert.Same(second,receipts.Current);
        receipts.ClearSame(second);
        Assert.Null(receipts.Current);
    }
}
