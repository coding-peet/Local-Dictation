using System.Windows;
using LocalDictation.Core;
namespace LocalDictation.App;

public sealed partial class AppController
{
    Dictionary<string,ReviewDocument>? privateReviews;
    public bool PrivateNextRecording {get;private set;}
    public bool IsPrivateSession=>active?.Review.IsPrivate==true||BannerReview?.IsPrivate==true;
    public int PrivateReviewCount=>privateReviews?.Values.Count(d=>d.InsertedAt==null&&d.OutputText.Length>0)??0;
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
    public void OpenLatestPrivateReview()
    {
        if(IsBusy)return;
        var document=privateReviews?.Values.Where(d=>d.InsertedAt==null&&d.OutputText.Length>0).OrderByDescending(d=>d.Created).FirstOrDefault();
        if(document!=null)OpenReview(document);
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
