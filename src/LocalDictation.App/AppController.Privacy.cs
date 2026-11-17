using System.Globalization;
using System.Text;
using System.Windows;
using LocalDictation.Core;
namespace LocalDictation.App;

public sealed record PrivateTranscriptChoice(string Id,DateTimeOffset Created,string Preview)
{
    public string Label=>$"{Created.LocalDateTime:g} · {Preview}";
}

public sealed partial class AppController
{
    Dictionary<string,ReviewDocument>? privateReviews;
    public bool PrivateNextRecording {get;private set;}
    public bool IsPrivateSession=>active?.Review.IsPrivate==true||BannerReview?.IsPrivate==true;
    static bool IsPendingPrivateReview(ReviewDocument document)=>document.IsPrivate&&document.InsertedAt==null&&document.OutputText.Length>0;
    IEnumerable<ReviewDocument> PendingPrivateReviews=>privateReviews?.Values.Where(IsPendingPrivateReview)??[];
    public int PrivateReviewCount=>PendingPrivateReviews.Count();
    public bool CanOpenPrivateReviews=>!IsBusy&&!IsApplyingReview&&!exiting&&reviewWindow?.IsApplying!=true;
    public IReadOnlyList<PrivateTranscriptChoice> PrivateReviewChoices=>Array.AsReadOnly(PendingPrivateReviews
        .OrderByDescending(d=>d.Created).ThenBy(d=>d.Id,StringComparer.Ordinal)
        .Select(d=>new PrivateTranscriptChoice(d.Id,d.Created,PrivatePreview(d.OutputText))).ToArray());
    static string PrivatePreview(string text)
    {
        var line=new StringBuilder();var separator=false;
        foreach(var rune in text.EnumerateRunes())
        {
            if(Rune.IsWhiteSpace(rune)||Rune.IsControl(rune)){separator=line.Length>0;continue;}
            if(separator){line.Append(' ');separator=false;}
            line.Append(rune.ToString());
        }
        var preview=line.ToString();var elements=StringInfo.ParseCombiningCharacters(preview);
        return elements.Length>72?preview[..elements[72]]+"…":preview;
    }
    public bool SetPrivateNextRecording(bool enabled)
    {
        if(IsBusy||IsApplyingReview||exiting)return false;
        PrivateNextRecording=enabled;Changed?.Invoke();return true;
    }
    void ClaimPrivateRecording(Run run)
    {
        if(!ReferenceEquals(active,run))throw new InvalidOperationException("Only the owned recording can claim private-next.");
        run.Review.IsPrivate=PrivateNextRecording;PrivateNextRecording=false;
        if(run.Review.IsPrivate)KeepPrivateReview(run.Review);
    }
    void KeepPrivateReview(ReviewDocument document)
    {
        privateReviews??=[];privateReviews[document.Id]=document;
    }
    public void OpenPrivateReview(string id)
    {
        if(!CanOpenPrivateReviews||string.IsNullOrEmpty(id))return;
        if(privateReviews?.TryGetValue(id,out var document)==true&&IsPendingPrivateReview(document))OpenReview(document);
    }
    public void OpenLatestPrivateReview()
    {
        var choice=PrivateReviewChoices.FirstOrDefault();
        if(choice!=null)OpenPrivateReview(choice.Id);
    }
    public void RequestDiscardPrivateReview(string id)
    {
        if(IsBusy||IsApplyingReview||privateReviews?.ContainsKey(id)!=true)return;
        if(MessageBox.Show("Discard this private transcript? It exists only in memory and cannot be recovered.","Discard private text",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        DismissReview(id);
    }
    bool ConfirmPrivateExit()=>MessageBox.Show("Private text exists only in memory. Stop any private recording, discard its transcript and exit? It cannot be recovered.",
        "Exit with private text",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
}
