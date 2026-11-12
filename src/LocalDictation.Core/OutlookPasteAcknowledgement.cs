namespace LocalDictation.Core;

public static class OutlookPasteAcknowledgement
{
    /// <summary>Recognizes only Outlook's observed initial final-paste boundary projection.</summary>
    public static bool IsKnownFinalPasteProjection(
        string? processName,
        string? beforeText,
        int start,
        string? oldOwnedText,
        string? nextText,
        string? observedText,
        int observedCaret,
        bool finalDelivery)
    {
        if (!DestinationDeliveryPolicy.InsertsAfterFinish(processName) ||
            !finalDelivery || start != 0 || oldOwnedText != "" ||
            string.IsNullOrEmpty(nextText) || nextText.Any(char.IsControl) ||
            observedCaret != nextText.Length || beforeText is null ||
            beforeText.Length < 3 || beforeText[0] != '\n' || beforeText[1] != '\n' ||
            beforeText[2] is '\r' or '\n')
            return false;

        // Match the whole observed document ordinally. Only the first of the two
        // pre-existing boundary LFs may be absent; the payload and signature stay exact.
        return string.Equals(observedText, nextText + beforeText[1..], StringComparison.Ordinal);
    }
}