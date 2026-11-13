namespace LocalDictation.Core;

public sealed record DictationItem(ReviewDocument Document,double? DurationSeconds,bool HasAudio,
    bool NeedsReview,bool IsUnsaved,string Preview)
{
    public string Id=>Document.Id;
    public string DurationLabel=>HasAudio?TimeSpan.FromSeconds(Math.Max(0,DurationSeconds??0)).ToString(@"m\:ss"):"Text only";
    public string State=>IsUnsaved?"Not saved":Document.InsertedAt!=null?"Inserted":NeedsReview?"Needs review":Document.OutputText.Length==0?"No text":"Saved";
    public string Label=>$"{Document.Created.LocalDateTime:g} · {(HasAudio?$"{DurationSeconds:0}s":"Text only")} · {State}\n{Preview}";
}

public static class DictationLibrary
{
    public static IReadOnlyList<DictationItem> Merge(IReadOnlyList<HistoryEntry> history,IReadOnlyList<ReviewDocument> pending,IReadOnlySet<string> unsaved)
    {
        var recordings=history.GroupBy(h=>h.Id).ToDictionary(g=>g.Key,g=>g.First());
        var drafts=pending.GroupBy(d=>d.Id).ToDictionary(g=>g.Key,g=>g.First());
        var rows=new List<DictationItem>();
        foreach(var id in recordings.Keys.Union(drafts.Keys))
        {
            recordings.TryGetValue(id,out var recording);drafts.TryGetValue(id,out var draft);
            // An acknowledged insertion is never resurrected by an older pending
            // snapshot. Otherwise the pending store owns the current manual edits.
            var document=recording?.Document.InsertedAt!=null&&draft?.InsertedAt==null?recording.Document:draft??recording!.Document;
            rows.Add(Item(document,recording?.DurationSeconds,draft!=null,unsaved.Contains(id)));
        }
        return rows.OrderByDescending(r=>r.Document.Created).ThenBy(r=>r.Id,StringComparer.Ordinal).ToList();
    }
    static DictationItem Item(ReviewDocument document,double? duration,bool pending,bool unsaved)
    {
        var preview=document.OutputText.ReplaceLineEndings(" ");
        return new(document,duration,duration!=null,pending&&document.InsertedAt==null,unsaved,preview[..Math.Min(80,preview.Length)]);
    }
}
