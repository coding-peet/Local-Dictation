using System.Globalization;
using System.Text;

namespace LocalDictation.Core;

public sealed record SettingsSearchEntry(string Id, string Category, string Label, string Description, string Keywords = "");

/// <summary>Searches display metadata without modifying settings or their navigation targets.</summary>
public static class SettingsSearch
{
    public static IReadOnlyList<SettingsSearchEntry> Find(IEnumerable<SettingsSearchEntry> entries, string query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var terms = Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return [];
        var phrase = string.Join(' ', terms);
        return entries.Select(entry =>
            {
                var label = Normalize(entry.Label);
                var text = string.Join(' ', label, Normalize(entry.Category), Normalize(entry.Description), Normalize(entry.Keywords));
                var rank = label == phrase ? 0 : label.StartsWith(phrase, StringComparison.Ordinal) ? 1
                    : label.Contains(phrase, StringComparison.Ordinal) ? 2 : 3;
                return (Entry: entry, Text: text, Rank: rank);
            })
            .Where(match => terms.All(term => match.Text.Contains(term, StringComparison.Ordinal)))
            .OrderBy(match => match.Rank)
            .Select(match => match.Entry)
            .ToArray();
    }

    static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var result = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
                result.Append(char.ToLowerInvariant(character));
        return result.ToString();
    }
}
