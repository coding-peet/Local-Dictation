using System.Text.Json.Serialization;

namespace LocalDictation.Core;

public sealed record ReviewPiece(string Id, string Primary, string Verification, bool Different);
public sealed record ReviewChoice(string Kind, string? Custom = null);
public sealed record ReviewSnapshot(Dictionary<string, ReviewChoice> Choices, string? ManualFinalDraft);

public sealed class ReviewDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Reason { get; set; } = "Review before inserting";
    public string PrimaryModel { get; set; } = "";
    public string? VerificationModel { get; set; }
    public string Primary { get; set; } = "";
    public string Verification { get; set; } = "";
    public List<ReviewPiece> Pieces { get; set; } = [];
    public Dictionary<string, ReviewChoice> Choices { get; set; } = [];
    public Dictionary<string, string> CustomDrafts { get; set; } = [];
    public List<ReviewSnapshot> History { get; set; } = [];
    public string? ManualFinalDraft { get; set; }
    public string? PreviousManualDraft { get; set; }
    public List<string> ManualDraftArchive { get; set; } = [];

    [JsonIgnore] public int UnresolvedCount => Pieces.Count(p => p.Different && !Choices.ContainsKey(p.Id));
    [JsonIgnore] public string WorkingText => string.Concat(Pieces.Select(p =>
        Choices.TryGetValue(p.Id, out var choice)
            ? choice.Kind switch { "verification" => p.Verification, "custom" => choice.Custom ?? "", _ => p.Primary }
            : p.Primary));
    [JsonIgnore] public string OutputText => ManualFinalDraft ?? WorkingText;

    void Remember()
    {
        History.Add(new(new(Choices), ManualFinalDraft));
        if (History.Count > 100) History.RemoveAt(0);
    }

    public void Choose(string id, string kind, string? custom = null)
    {
        if (!Pieces.Any(p => p.Id == id && p.Different)) throw new ArgumentException("Unknown difference.");
        if (kind is not ("primary" or "verification" or "custom")) throw new ArgumentException("Unknown choice.");
        Remember();
        Choices[id] = new(kind, custom);
    }

    public void KeepRemaining()
    {
        Remember();
        foreach (var piece in Pieces.Where(p => p.Different))
            Choices.TryAdd(piece.Id, new("primary"));
    }

    public void Undo()
    {
        if (History.Count == 0) return;
        var previous = History[^1];
        History.RemoveAt(History.Count - 1);
        Choices = new(previous.Choices);
        // Choice history never replaces a later authoritative manual edit.
    }

    public void RebuildDraft()
    {
        if(ManualFinalDraft!=null)
        {
            if(!ManualDraftArchive.Contains(ManualFinalDraft))ManualDraftArchive.Add(ManualFinalDraft);
            PreviousManualDraft=ManualFinalDraft;
        }
        ManualFinalDraft=null;
    }

    public void SetPrimary(string text)
    {
        Primary = text;
        Verification = text;
        Pieces = [new("primary", text, text, false)];
        Choices.Clear();
    }
}
