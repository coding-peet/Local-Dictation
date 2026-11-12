using LocalDictation.Core;
using Xunit;

public class OutlookPasteAcknowledgementTests
{
    const string Before = "\n\nS pozdravom,\nInžinier Ľuboš";
    const string Payload = "Ďakujem.";
    const string Observed = "Ďakujem.\nS pozdravom,\nInžinier Ľuboš";

    [Fact]
    public void RecognizesOnlyTheOneBoundaryLfProjectionInTheObservedStructuralShape()
    {
        const string payload = "Ahoj posielam ti správu. Ďakujem za tvoju pomoc.";
        const string before = "\n\nS pozdravom,\nTestovací inžinier\nExample Engineering\nteam\nExample street 10\n1000 Test City\nTel: +000 123 456 789\nEmail: engineer@example.invalid\nwww.example.invalid\n\nThis synthetic signature is retained exactly, including accents: ľščťžýáíé";
        const string observed = "Ahoj posielam ti správu. Ďakujem za tvoju pomoc.\nS pozdravom,\nTestovací inžinier\nExample Engineering\nteam\nExample street 10\n1000 Test City\nTel: +000 123 456 789\nEmail: engineer@example.invalid\nwww.example.invalid\n\nThis synthetic signature is retained exactly, including accents: ľščťžýáíé";

        Assert.Equal(48, payload.Length);
        Assert.Equal(241, before.Length);
        Assert.Equal(288, observed.Length);
        Assert.True(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "OUTLOOK", before, 0, "", payload, observed, 48, true));
    }

    [Theory]
    [InlineData("OUTLOOK")]
    [InlineData("outlook")]
    [InlineData("OutLook")]
    [InlineData("olk")]
    [InlineData("OLK")]
    [InlineData("oLk")]
    public void RecognizesKnownOutlookProcessesOnly(string processName)
    {
        Assert.True(Check(processName: processName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("notepad")]
    [InlineData("chrome")]
    [InlineData("OUTLOOK.EXE")]
    [InlineData("olk.exe")]
    [InlineData(" outlook")]
    [InlineData("outlook ")]
    [InlineData("olk-preview")]
    [InlineData("my-outlook-window")]
    [InlineData("Inbox - Outlook")]
    [InlineData("C:\\Program Files\\Microsoft Office\\OUTLOOK.EXE")]
    public void RejectsUnknownOrNonExactProcessNames(string? processName)
    {
        Assert.False(Check(processName: processName));
    }

    [Fact]
    public void RejectsLiveOrPartialDelivery()
    {
        Assert.False(Check(finalDelivery: false));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void RejectsAnyInsertionStartOtherThanDocumentStart(int start)
    {
        Assert.False(Check(start: start));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("old")]
    [InlineData(" ")]
    [InlineData("\n")]
    public void RejectsExistingOrUnknownOwnedText(string? oldOwnedText)
    {
        Assert.False(Check(oldOwnedText: oldOwnedText));
    }

    [Theory]
    [InlineData(null, "Text\nSignature")]
    [InlineData("", "Text")]
    [InlineData("\n", "Text")]
    [InlineData("\n\n", "Text\n")]
    [InlineData("\nSignature", "TextSignature")]
    [InlineData("\n\n\nSignature", "Text\n\nSignature")]
    [InlineData("\r\n\r\nSignature", "Text\n\r\nSignature")]
    [InlineData("\n\r\nSignature", "Text\r\nSignature")]
    [InlineData("\n\n\rSignature", "Text\n\rSignature")]
    [InlineData("\n \nSignature", "Text \nSignature")]
    [InlineData("x\n\nSignature", "Text\n\nSignature")]
    [InlineData("\u00a0\nSignature", "Text\nSignature")]
    public void RejectsBeforeTextWithoutExactlyTwoInitialLfAndASignatureCharacter(
        string? beforeText, string observedText)
    {
        Assert.False(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "OUTLOOK", beforeText, 0, "", "Text", observedText, 4, true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("Text\n")]
    [InlineData("Text\r\n")]
    [InlineData("Text\r")]
    [InlineData("Text\t")]
    [InlineData("Text\0")]
    [InlineData("Text\u0001")]
    [InlineData("Text\u007f")]
    [InlineData("Text\u0085")]
    public void RejectsEmptyUnknownOrControlCharacterPayload(string? nextText)
    {
        var observed = nextText + "\nS pozdravom,\nInžinier Ľuboš";
        Assert.False(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "OUTLOOK", Before, 0, "", nextText, observed, nextText?.Length ?? 0, true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void RejectsCaretThatDoesNotExactlyMatchTheWholePayloadLength(int observedCaret)
    {
        Assert.False(Check(observedCaret: observedCaret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ďakujem.")]
    [InlineData("Ďakujem.\n\nS pozdravom,\nInžinier Ľuboš")]
    [InlineData("Ďakujem.S pozdravom,\nInžinier Ľuboš")]
    [InlineData("Ďakujem.\r\nS pozdravom,\nInžinier Ľuboš")]
    [InlineData("Ďakujem.\u00a0S pozdravom,\nInžinier Ľuboš")]
    [InlineData("Ďakujem\nS pozdravom,\nInžinier Ľuboš")]
    [InlineData("Ďakujem!\nS pozdravom,\nInžinier Ľuboš")]
    [InlineData("XĎakujem.\nS pozdravom,\nInžinier Ľuboš")]
    [InlineData("Ďakujem.\nS pozdravom,\nInžinier ĽubošX")]
    [InlineData("Ďakujem.\nS pozdravom,\nInžinier Ľubo")]
    [InlineData("Dakujem.\nS pozdravom,\nInžinier Ľuboš")]
    [InlineData("Ďakujem.\nS pozdravom,\nInžinier Luboš")]
    [InlineData("Ďakujem.\nS pozdravom,Inžinier Ľuboš")]
    [InlineData("Ďakujem.\nS pozdravom,\r\nInžinier Ľuboš")]
    [InlineData("Ďakujem.\nS pozdravom,\u00a0Inžinier Ľuboš")]
    public void RejectsAnyReadbackOtherThanTheSingleMissingBoundaryLf(string? observedText)
    {
        Assert.False(Check(observedText: observedText));
    }

    [Fact]
    public void KeepsPayloadUnicodeNbspAndSpacesExact()
    {
        Assert.True(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "olk", "\n\nSignature", 0, "", " Ľúbim\u00a0čaj. ",
            " Ľúbim\u00a0čaj. \nSignature", 12, true));
        Assert.False(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "olk", "\n\nSignature", 0, "", " Ľúbim\u00a0čaj. ",
            " Ľúbim čaj. \nSignature", 12, true));
    }

    [Fact]
    public void KeepsLaterSignatureFormattingExact()
    {
        Assert.True(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "OUTLOOK", "\n\n\tSignature\r\n\u00a0End\n\n", 0, "", "Text",
            "Text\n\tSignature\r\n\u00a0End\n\n", 4, true));
        Assert.False(OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            "OUTLOOK", "\n\n\tSignature\r\n\u00a0End\n\n", 0, "", "Text",
            "Text\n\tSignature\n End\n\n", 4, true));
    }

    [Fact]
    public void RejectsPayloadMismatchEvenWhenReadbackHasTheSameLength()
    {
        Assert.False(Check(nextText: "Ďakujem!"));
    }

    static bool Check(
        string? processName = "OUTLOOK",
        int start = 0,
        string? oldOwnedText = "",
        string? nextText = Payload,
        string? observedText = Observed,
        int observedCaret = 8,
        bool finalDelivery = true) =>
        OutlookPasteAcknowledgement.IsKnownFinalPasteProjection(
            processName, Before, start, oldOwnedText, nextText,
            observedText, observedCaret, finalDelivery);
}