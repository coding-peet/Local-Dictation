using System.Globalization;
using System.Text;

namespace LocalDictation.Core;

public sealed record HistoryQuery(string Text="",DateOnly? From=null,DateOnly? To=null,
    string? Model=null,bool NeedsReview=false,bool TextOnly=false,bool Inserted=false);

/// <summary>In-memory queries over complete original and edited text; never creates an index on disk.</summary>
public static class DictationSearch
{
    public static IReadOnlyList<DictationItem> Find(IEnumerable<DictationItem> items,HistoryQuery query)
    {
        if(query.From>query.To)throw new ArgumentException("From must be on or before To.");
        var terms=Normalize(query.Text).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        return items.Where(item=>
        {
            var d=item.Document;var day=DateOnly.FromDateTime(d.Created.LocalDateTime);
            if(query.From is {} from&&day<from||query.To is {} to&&day>to)return false;
            if(query.NeedsReview&&!item.NeedsReview||query.TextOnly&&item.HasAudio||query.Inserted&&d.InsertedAt==null)return false;
            if(!string.IsNullOrEmpty(query.Model)&&!string.Equals(query.Model,d.PrimaryModel,StringComparison.OrdinalIgnoreCase)&&
                !string.Equals(query.Model,d.VerificationModel,StringComparison.OrdinalIgnoreCase))return false;
            if(terms.Length==0)return true;
            var text=Normalize(string.Join(" ",Text(d)));
            return terms.All(t=>text.Contains(t,StringComparison.Ordinal));
        }).ToArray();
    }
    static IEnumerable<string?> Text(ReviewDocument d)
    {
        yield return d.OutputText;yield return d.Primary;yield return d.Verification;
        yield return d.PreviousManualDraft;yield return d.PrimaryModel;yield return d.VerificationModel;
        yield return d.Created.LocalDateTime.ToString("d");yield return d.Created.LocalDateTime.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        foreach(var v in d.ManualDraftArchive)yield return v;
        foreach(var v in d.CustomDrafts.Values)yield return v;
        foreach(var h in d.History){yield return h.ManualFinalDraft;foreach(var c in h.Choices.Values)yield return c.Custom;}
    }
    static string Normalize(string? text)
    {
        if(string.IsNullOrEmpty(text))return "";
        var result=new StringBuilder(text.Length);
        foreach(var c in text.Normalize(NormalizationForm.FormD))
            if(CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))result.Append(char.ToLowerInvariant(c));
        return result.ToString();
    }
}
