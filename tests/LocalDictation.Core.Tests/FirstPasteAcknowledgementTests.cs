using LocalDictation.Core;
using Xunit;

public class FirstPasteAcknowledgementTests
{
    const string Projection = "Write a message\n";
    const string Payload = "Prvá veta bude.";
    const string Field = "captured-field";
    static FirstPasteEvidence Proof(int caret = 0) => new(
        "chrome", "Chrome", "ProseMirror ProseMirror-focused", 50004,
        Field, Projection, caret, false, true, 4);

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void FirstCollapsedPasteRebasesTheCompletePayloadAtEitherDocumentEdge(int caret)
    {
        var guard = new TargetGuard(Field, Projection, caret);
        Assert.Equal(16, Projection.Length);
        Assert.Equal(15, Payload.Length);
        Assert.True(guard.TryAcknowledgeFirstPaste(Proof(caret), Payload, Field, Payload, 15));
        Assert.Equal(0, guard.Start);
        Assert.Equal(Payload, guard.OwnedText);
        Assert.Equal(Payload, guard.ExpectedText);
        Assert.True(guard.CanReplace(Field, Payload, 15, 15));
        Assert.False(guard.CanResume(Field, Projection));
    }

    [Fact]
    public void PauseContinuationAndEarlierWordCorrectionKeepTheWholeObservedDraftOwned()
    {
        var guard = new TargetGuard(Field, Projection, 16);
        Assert.True(guard.TryAcknowledgeFirstPaste(Proof(16), Payload, Field, Payload, 15));
        Assert.True(guard.CanResume(Field, Payload));
        const string continued = "Prvá veta bude. Pokračujem po pauze.";
        Assert.Equal(continued, guard.ReplacedText(continued));
        guard.Applied(continued, continued, continued.Length);
        const string corrected = "Opravená veta bude. Pokračujem po pauze.";
        Assert.Equal(corrected, guard.ReplacedText(corrected));
        guard.Applied(corrected, corrected, corrected.Length);
        Assert.True(guard.CanReplace(Field, corrected, corrected.Length, corrected.Length));
        Assert.False(guard.CanResume(Field, "external " + corrected));
        Assert.False(guard.CanResume("different-field", corrected));
    }

    [Theory]
    [InlineData(0, "Prvá veta bude.Write a message\n")]
    [InlineData(16, "Write a message\nPrvá veta bude.")]
    public void RealTextWithIdenticalPlaceholderWordsRemainsInTheOrdinaryBaseline(int caret, string observed)
    {
        var guard = new TargetGuard(Field, Projection, caret);
        Assert.False(guard.TryAcknowledgeFirstPaste(Proof(caret), Payload, Field, observed, caret + 15));
        Assert.Equal(observed, guard.ReplacedText(Payload));
        guard.Applied(Payload, observed, caret + 15);
        Assert.Equal(Projection, guard.ReplacedText(""));
    }

    [Fact]
    public void RejectsAnyChangedBeforeIdentityTextCaretOrSelectedRange()
    {
        var proof = Proof();
        FirstPasteEvidence[] changed = [
            proof with { Identity = "different-field" },
            proof with { Text = "changed" },
            proof with { Caret = 16 },
            proof with { SelectionMade = true }
        ];
        foreach (var evidence in changed) Reject(evidence);
        Reject(proof, observedIdentity: "different-field");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(-1)]
    [InlineData(17)]
    public void RejectsInitialCaretAwayFromEitherDocumentEdge(int caret) => Reject(Proof(caret), start: caret);

    [Theory]
    [InlineData("ProseMirror")]
    [InlineData("ProseMirror ProseMirror-focused")]
    [InlineData("ProseMirror-focused\tProseMirror")]
    public void RecognizesOnlyExactRawProviderClassTokens(string className)
    {
        var guard = new TargetGuard(Field, Projection, 0);
        Assert.True(guard.TryAcknowledgeFirstPaste(Proof() with { ClassName = className }, Payload, Field, Payload, 15));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ProseMirror-focused")]
    [InlineData("ProseMirrorOther")]
    [InlineData("ProseMirrorProseMirror-focused")]
    [InlineData("prosemirror")]
    [InlineData("ProseMirror other")]
    [InlineData("ProseMirror ProseMirror")]
    [InlineData("ProseMirror ProseMirror-focused extra")]
    public void RejectsMissingMisspelledDuplicateOrExtraProviderTokens(string className) =>
        Reject(Proof() with { ClassName = className });

    [Theory]
    [InlineData("Codex")]
    [InlineData("msedge")]
    [InlineData(null)]
    public void KnownRawProviderMayBeHostedByTheCapturedNonChromeApplication(string? processName)
    {
        var guard = new TargetGuard(Field, Projection, 0);
        Assert.True(guard.TryAcknowledgeFirstPaste(Proof() with { ProcessName = processName }, Payload, Field, Payload, 15));
    }

    [Fact]
    public void RejectsUnrecognizedProvider()
    {
        var proof = Proof();
        FirstPasteEvidence[] changed = [
            proof with { FrameworkId = "WPF" },
            proof with { ClassName = "ProseMirrorOther" }, proof with { ControlTypeId = 50030 }
        ];
        foreach (var evidence in changed) Reject(evidence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void RejectsPartialUnknownOrExtraNativeDispatch(int queued) => Reject(Proof() with { QueuedInputEvents = queued });

    [Fact]
    public void RejectsKeyboardOrDeleteTransport() => Reject(Proof() with { NativeClipboardPaste = false });

    [Theory]
    [InlineData("prefix Prvá veta bude.", 22)]
    [InlineData("Prvá veta bude. suffix", 15)]
    [InlineData("Prvá veta bude", 14)]
    [InlineData("Prvá veta bude!", 15)]
    [InlineData("Prvá veta bude.", 0)]
    [InlineData("Prvá veta bude.", 14)]
    [InlineData("Prvá veta bude.", 16)]
    public void RejectsAnySurroundingTextPartialPayloadOrWrongEndingCaret(string observed, int caret) =>
        Reject(Proof(), observedText: observed, observedCaret: caret);

    [Theory]
    [InlineData("")]
    [InlineData("bad\ntext")]
    [InlineData("bad\ttext")]
    public void RejectsEmptyOrControlCharacterPayload(string payload) =>
        Reject(Proof(), payload: payload, observedText: payload, observedCaret: payload.Length);

    [Fact]
    public void RejectsUnchangedDocumentEvenWhenItsWordingEqualsThePayload()
    {
        var guard = new TargetGuard(Field, "Real text", 0);
        Assert.False(guard.TryAcknowledgeFirstPaste(Proof() with { Text = "Real text" },
            "Real text", Field, "Real text", 9));
        Assert.Equal("", guard.OwnedText);
    }

    [Fact]
    public void RejectsNonFirstInsertionEvenAfterACompleteWithdrawal()
    {
        var guard = new TargetGuard(Field, Projection, 0);
        guard.Applied("old", "old" + Projection, 3);
        Assert.False(guard.TryAcknowledgeFirstPaste(Proof(), Payload, Field, Payload, 15));
        guard.Applied("", Projection, 0);
        Assert.False(guard.TryAcknowledgeFirstPaste(Proof(), Payload, Field, Payload, 15));
        Assert.Equal(Projection, guard.ExpectedText);
    }

    [Fact]
    public void RejectsInvalidatedGuard()
    {
        var guard = new TargetGuard(Field, Projection, 0);
        guard.Invalidate();
        Assert.False(guard.TryAcknowledgeFirstPaste(Proof(), Payload, Field, Payload, 15));
    }

    static void Reject(FirstPasteEvidence proof, int start = 0, string payload = Payload,
        string observedIdentity = Field, string observedText = Payload, int observedCaret = 15)
    {
        var guard = new TargetGuard(Field, Projection, start);
        Assert.False(guard.TryAcknowledgeFirstPaste(proof, payload, observedIdentity, observedText, observedCaret));
        Assert.Equal(start, guard.Start);
        Assert.Equal(Projection, guard.ExpectedText);
        Assert.Equal("", guard.OwnedText);
    }
}
