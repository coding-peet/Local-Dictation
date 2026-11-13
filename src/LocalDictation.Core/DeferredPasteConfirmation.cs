namespace LocalDictation.Core;

public enum PasteAcknowledgementKind { None, Exact, OutlookParagraphProjection, FirstPasteBaseline }

/// <summary>Memory-only receipt facts from the single paste already dispatched.</summary>
public sealed record PasteAcknowledgementSnapshot(string Identity, string? ProcessName,
    string BeforeText, int Start, string OldOwnedText, string NextText, string ExpectedText,
    bool IsFinal, FirstPasteEvidence? FirstPasteEvidence);

public readonly record struct DeferredPasteEligibility(bool NativeClipboardPaste, int QueuedEvents,
    TimeSpan Age, int Generation, int ExpectedGeneration, long InvalidationVersion,
    long ExpectedInvalidationVersion, bool HeldLeaseCurrent, bool TargetCurrent,
    bool ProviderCurrent, bool Cancelled);

public static class DeferredPasteConfirmation
{
    public static readonly TimeSpan MaximumAge=TimeSpan.FromSeconds(10);
    public static bool CanProbe(DeferredPasteEligibility evidence) =>
        evidence.NativeClipboardPaste&&evidence.QueuedEvents==4&&
        evidence.Age>=TimeSpan.Zero&&evidence.Age<MaximumAge&&
        evidence.Generation==evidence.ExpectedGeneration&&
        evidence.InvalidationVersion==evidence.ExpectedInvalidationVersion&&
        evidence.HeldLeaseCurrent&&evidence.TargetCurrent&&evidence.ProviderCurrent&&!evidence.Cancelled;
    public static PasteAcknowledgementKind TryAcknowledge(TargetGuard guard, PasteAcknowledgementSnapshot paste,
        string observedIdentity, string observedText, int observedCaret)
    {
        if(observedIdentity!=paste.Identity||!guard.CanResume(paste.Identity,paste.BeforeText)||
            guard.Start!=paste.Start||guard.OwnedText!=paste.OldOwnedText||
            paste.Start<0||paste.Start>paste.BeforeText.Length-paste.OldOwnedText.Length||
            guard.Caret!=paste.Start+paste.OldOwnedText.Length||guard.ReplacedText(paste.NextText)!=paste.ExpectedText)
            return PasteAcknowledgementKind.None;
        if(observedCaret==paste.Start+paste.NextText.Length)
        {
            if(observedText==paste.ExpectedText)
            {
                guard.Applied(paste.NextText,observedText,observedCaret);
                return PasteAcknowledgementKind.Exact;
            }
            if(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(paste.ProcessName,paste.BeforeText,
                paste.Start,paste.OldOwnedText,paste.NextText,observedText,observedCaret,paste.IsFinal))
            {
                guard.Applied(paste.NextText,observedText,observedCaret);
                return PasteAcknowledgementKind.OutlookParagraphProjection;
            }
        }
        return paste.FirstPasteEvidence is { } proof&&guard.TryAcknowledgeFirstPaste(proof,paste.NextText,
            observedIdentity,observedText,observedCaret)?PasteAcknowledgementKind.FirstPasteBaseline:PasteAcknowledgementKind.None;
    }
}
