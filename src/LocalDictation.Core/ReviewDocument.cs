using System.Text.Json.Serialization;

namespace LocalDictation.Core;

public sealed record ReviewPiece(string Id, string Primary, string Verification, bool Different);
public sealed record ReviewChoice(string Kind, string? Custom = null);
public sealed record ReviewSnapshot(Dictionary<string, ReviewChoice> Choices, string? ManualFinalDraft);
public sealed record ReviewInsertionResult(bool Success,string Message);

public sealed class ReviewDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Reason { get; set; } = "Review before inserting";
    public DateTimeOffset? InsertedAt {get;set;}
    public bool DraftWithdrawn {get;set;}
    [JsonIgnore] public bool CanInsert=>InsertedAt==null&&UnresolvedCount==0;
    public ReviewProvenance? Provenance {get;set;}
    public string PrimaryModel { get; set; } = "";
    public string RequestedLanguage {get;set;}="";
    public string? Language {get;set;}
    public string? LanguageMode {get;set;}
    public string? RetranscriptionOf {get;set;}
    public string? VerificationModel { get; set; }
    public string Primary { get; set; } = "";
    public string Verification { get; set; } = "";
    public bool VerificationCompleted {get;set;}
    public List<ReviewPiece> Pieces { get; set; } = [];
    public Dictionary<string, ReviewChoice> Choices { get; set; } = [];
    public Dictionary<string, string> CustomDrafts { get; set; } = [];
    public List<ReviewSnapshot> History { get; set; } = [];
    public string? ManualFinalDraft { get; set; }
    public string? PreviousManualDraft { get; set; }
    public List<string> ManualDraftArchive { get; set; } = [];

    [JsonIgnore] public bool HasCompletedVerification => VerificationCompleted ||
        VerificationModel!=null && Pieces.Count>0 &&
        Pieces.All(p=>p.Id.Length==16&&p.Id.All(Uri.IsHexDigit)) &&
        string.Concat(Pieces.Select(p=>p.Primary))==Primary &&
        string.Concat(Pieces.Select(p=>p.Verification))==Verification;
    [JsonIgnore] public int UnresolvedCount => Pieces.Count(p => p.Different && !Choices.ContainsKey(p.Id));
    [JsonIgnore] public string WorkingText => string.Concat(Pieces.Select(p =>
        Choices.TryGetValue(p.Id, out var choice)
            ? choice.Kind switch { "verification" => p.Verification, "custom" => choice.Custom ?? "", _ => p.Primary }
            : p.Primary));
    [JsonIgnore] public string OutputText => ManualFinalDraft ?? WorkingText;

    public void AttachProvenance(ReviewProvenance value)
    {
        if(Provenance!=null&&((Provenance with {ComparisonId=null})!=(value with {ComparisonId=null})||
            Provenance.ComparisonId!=null&&Provenance.ComparisonId!=value.ComparisonId))
            throw new InvalidOperationException("The recording provenance cannot change.");
        if(PrimaryModel.Length>0&&PrimaryModel!=value.Primary.Id||VerificationModel!=value.Verification?.Id)
            throw new InvalidOperationException("The recording model identity does not match.");
        Provenance=value;
    }

    public void CompletePendingVerificationProvenance(ReviewProvenance value)
    {
        // A post-hoc pass pins its original recording before inference; only the
        // still-unknown second revision may be completed by the actual response.
        if(Provenance is {ComparisonId:null,Verification:{Revision:null}} pending&&
            pending.Verification.Id==value.Verification?.Id&&PrimaryModel==value.Primary.Id&&VerificationModel==value.Verification?.Id&&
            (pending with{Verification=value.Verification,ComparisonId=value.ComparisonId})==value)
        {Provenance=value;return;}
        AttachProvenance(value);
    }

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
        Verification = VerificationModel is null?text:"";
        VerificationCompleted=false;
        Pieces = [new("primary", text, text, false)];
        Choices.Clear();
    }
}
