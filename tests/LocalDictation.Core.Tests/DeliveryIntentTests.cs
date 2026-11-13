using LocalDictation.Core;
using Xunit;

public class DeliveryIntentTests
{
    [Fact]
    public void AutomaticFinalInsertionStillAcknowledgesTheKnownOutlookParagraphProjection()
    {
        var intent = DeliveryIntent.AutomaticFinal;
        Assert.False(intent.RestoreFocus);
        Assert.True(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "OUTLOOK", "\n\nSignature", 0, "", "Final text", "Final text\nSignature", 10, intent.IsFinal));
    }

    [Fact]
    public void ExplicitFinalInsertionMayRestoreFocusWithoutChangingAcknowledgement()
    {
        var intent = DeliveryIntent.ExplicitFinal;
        Assert.True(intent.RestoreFocus);
        Assert.True(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "olk", "\n\nSignature", 0, "", "Final text", "Final text\nSignature", 10, intent.IsFinal));
    }

    [Fact]
    public void LiveUpdatesNeverUseTheFinalOnlyOutlookProjection()
    {
        var intent = DeliveryIntent.Live;
        Assert.False(intent.RestoreFocus);
        Assert.False(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "olk", "\n\nSignature", 0, "", "Final text", "Final text\nSignature", 10, intent.IsFinal));
    }
}
