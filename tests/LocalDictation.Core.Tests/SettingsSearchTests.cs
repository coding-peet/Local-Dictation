using LocalDictation.Core;
using Xunit;

public class SettingsSearchTests
{
    static readonly SettingsSearchEntry[] Entries =
    [
        new("microphone", "Dictation", "Microphone", "Choose an audio input device.", "mic capture recording"),
        new("language", "Dictation", "Input language", "Recognize Slovak and English speech.", "locale jazyk slovenčina"),
        new("verification", "Dictation", "Verify after recording", "Compare the transcript with a second model.", "accuracy checking"),
        new("appearance", "Settings", "Appearance", "Choose the application theme.", "light dark colour")
    ];

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void BlankQueryReturnsNoResults(string query)
    {
        Assert.Empty(SettingsSearch.Find(Entries, query));
    }

    [Theory]
    [InlineData("microphone", "microphone")]
    [InlineData("SETTINGS", "appearance")]
    [InlineData("transcript", "verification")]
    [InlineData("capture", "microphone")]
    [InlineData("SLOVENCINA", "language")]
    [InlineData("slovenc\u030cina", "language")]
    public void SearchesVisibleTextAndKeywordsIgnoringCaseAndDiacritics(string query, string id)
    {
        Assert.Equal(id, Assert.Single(SettingsSearch.Find(Entries, query)).Id);
    }

    [Fact]
    public void EveryTermMustMatchButCanBelongToDifferentFields()
    {
        Assert.Equal("language", Assert.Single(SettingsSearch.Find(Entries, "  DICTATION\tenglish  jazyk ")).Id);
        Assert.Empty(SettingsSearch.Find(Entries, "microphone english"));
    }

    [Fact]
    public void ExactAndPrefixLabelsRankBeforeSubstringAndDescriptionMatches()
    {
        SettingsSearchEntry[] entries =
        [
            new("description", "Settings", "Speech", "Choose a model."),
            new("substring", "Dictation", "Verification model", ""),
            new("prefix", "Models", "Model downloads", ""),
            new("exact", "Dictation", "MÓDEL", "")
        ];
        Assert.Equal(new[] { "exact", "prefix", "substring", "description" },
            SettingsSearch.Find(entries, "MODEL").Select(entry => entry.Id));
    }

    [Fact]
    public void EqualRankRetainsEntryOrderAndExactNavigationIdentity()
    {
        var first = new SettingsSearchEntry("primary-field", "Dictation", "Primary model", "");
        var second = new SettingsSearchEntry("verifier-parent", "Dictation", "Verification model", "");
        var results = SettingsSearch.Find(new[] { first, second }, "model");
        Assert.Equal(new[] { first, second }, results);
        Assert.Same(first, results[0]);
        Assert.Same(second, results[1]);
    }

    [Fact]
    public void InternalTargetIdDoesNotCreateAUserFacingMatch()
    {
        Assert.Empty(SettingsSearch.Find(Entries, "appearance" + "-internal"));
        Assert.Empty(SettingsSearch.Find(new[] { new SettingsSearchEntry("internal-token", "Settings", "Theme", "") }, "internal-token"));
    }

    [Fact]
    public void EmptyCatalogReturnsNoResults()
    {
        Assert.Empty(SettingsSearch.Find(Array.Empty<SettingsSearchEntry>(), "model"));
    }
}
