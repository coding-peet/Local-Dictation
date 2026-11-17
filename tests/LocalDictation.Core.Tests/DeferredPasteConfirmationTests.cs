using LocalDictation.Core;
using Xunit;

public class DeferredPasteConfirmationTests
{
    static DeferredPasteEligibility Eligible => new(true,4,TimeSpan.FromSeconds(2),3,3,7,7,true,true,true,false);
    static PasteAcknowledgementSnapshot Ordinary => new("field","notepad","before  after",7,"","draft",
        "before draft after",false,null);

    [Fact]
    public void UnchangedExactDelayedReadbackAcknowledgesTheAlreadyDispatchedDraft()
    {
        Assert.True(DeferredPasteConfirmation.CanProbe(Eligible));
        var guard=new TargetGuard("field","before  after",7);
        Assert.Equal(PasteAcknowledgementKind.Exact,DeferredPasteConfirmation.TryAcknowledge(
            guard,Ordinary,"field","before draft after",12));
        Assert.Equal("draft",guard.OwnedText);
        Assert.Equal("before  after",guard.ReplacedText(""));
    }

    [Fact]
    public void DelayedKnownOutlookParagraphProjectionUsesTheExistingFinalPolicy()
    {
        var guard=new TargetGuard("field","\n\nSignature",0);
        var paste=new PasteAcknowledgementSnapshot("field","OUTLOOK","\n\nSignature",0,"","Hello.",
            "Hello.\n\nSignature",true,null);
        Assert.Equal(PasteAcknowledgementKind.OutlookParagraphProjection,DeferredPasteConfirmation.TryAcknowledge(
            guard,paste,"field","Hello.\nSignature",6));
        Assert.Equal("Hello.\nSignature",guard.ExpectedText);
    }

    [Fact]
    public void DelayedFirstProviderPasteRebasesUsingTheExistingStrictProof()
    {
        const string before="Write a message\n";
        var guard=new TargetGuard("field",before,16);
        var first=new FirstPasteEvidence("Codex","Chrome","ProseMirror ProseMirror-focused",50004,
            "field",before,16,false,true,4);
        var paste=new PasteAcknowledgementSnapshot("field","Codex",before,16,"","Prvá veta bude.",
            "Write a message\nPrvá veta bude.",false,first);
        Assert.Equal(PasteAcknowledgementKind.FirstPasteBaseline,DeferredPasteConfirmation.TryAcknowledge(
            guard,paste,"field","Prvá veta bude.",15));
        Assert.Equal(0,guard.Start);
        Assert.Equal("Opravená veta.",guard.ReplacedText("Opravená veta."));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(5)]
    public void PartialUnknownOrExtraDispatchCannotStartAProbe(int events) =>
        Assert.False(DeferredPasteConfirmation.CanProbe(Eligible with {QueuedEvents=events}));

    [Fact]
    public void InterruptedExpiredOrSupersededReceiptCannotStartAProbe()
    {
        DeferredPasteEligibility[] denied=[
            Eligible with {NativeClipboardPaste=false}, Eligible with {Generation=4},
            Eligible with {InvalidationVersion=8}, Eligible with {AttemptCurrent=false},
            Eligible with {TargetCurrent=false}, Eligible with {ProviderCurrent=false},
            Eligible with {Cancelled=true}, Eligible with {Age=TimeSpan.FromSeconds(10)},
            Eligible with {Age=TimeSpan.FromSeconds(11)}, Eligible with {Age=TimeSpan.FromMilliseconds(-1)}
        ];
        foreach(var state in denied)Assert.False(DeferredPasteConfirmation.CanProbe(state));
    }

    [Theory]
    [InlineData("other","before draft after",12)]
    [InlineData("field","before changed after",12)]
    [InlineData("field","before draft after",11)]
    [InlineData("field","before draft after",13)]
    [InlineData("field","draft",5)]
    public void ChangedTargetWholeTextOrCaretCannotAcknowledge(string identity,string text,int caret)
    {
        var guard=new TargetGuard("field","before  after",7);
        Assert.Equal(PasteAcknowledgementKind.None,DeferredPasteConfirmation.TryAcknowledge(guard,Ordinary,identity,text,caret));
        Assert.Equal("",guard.OwnedText);
        Assert.Equal("before  after",guard.ExpectedText);
    }

    [Fact]
    public void GuardBaselineChangesOrInvalidationCannotBeOverwritten()
    {
        var guard=new TargetGuard("field","before  after",7);
        guard.Applied("newer","before newer after",12);
        Assert.Equal(PasteAcknowledgementKind.None,DeferredPasteConfirmation.TryAcknowledge(
            guard,Ordinary,"field","before draft after",12));
        Assert.Equal("newer",guard.OwnedText);
        guard=new TargetGuard("field","before  after",7);guard.Invalidate();
        Assert.Equal(PasteAcknowledgementKind.None,DeferredPasteConfirmation.TryAcknowledge(
            guard,Ordinary,"field","before draft after",12));
    }

    [Fact]
    public void OutlookExceptionsAreNotExpandedToLiveOrUnknownTextDifferences()
    {
        var paste=new PasteAcknowledgementSnapshot("field","OUTLOOK","\n\nSignature",0,"","Hello.",
            "Hello.\n\nSignature",false,null);
        var guard=new TargetGuard("field","\n\nSignature",0);
        Assert.Equal(PasteAcknowledgementKind.None,DeferredPasteConfirmation.TryAcknowledge(
            guard,paste,"field","Hello.\nSignature",6));
        Assert.Equal(PasteAcknowledgementKind.None,DeferredPasteConfirmation.TryAcknowledge(
            guard,paste with {IsFinal=true},"field","Hello.Signature",6));
    }
}
