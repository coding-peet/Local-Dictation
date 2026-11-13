namespace LocalDictation.Core;

public sealed record SavedVerificationResult(string Primary,string Verification,List<ReviewPiece> Pieces,
    string PrimaryModel,string? PrimaryRevision,string VerificationModel,string? VerificationRevision,
    string ComparisonId,string? Language,string? LanguageMode,double Duration);

public static class SavedVerification
{
    public static ReviewDocument Begin(ReviewDocument source,string verifier)
    {
        if(string.IsNullOrWhiteSpace(source.Primary)||string.IsNullOrWhiteSpace(source.PrimaryModel))
            throw new ArgumentException("The original transcript and model identity are required.");
        if(string.IsNullOrWhiteSpace(verifier)||verifier==source.PrimaryModel)
            throw new ArgumentException("Choose a different second model.");
        var document=new ReviewDocument{PrimaryModel=source.PrimaryModel,VerificationModel=verifier,
            RequestedLanguage=source.RequestedLanguage,Language=source.Language,LanguageMode=source.LanguageMode,
            RetranscriptionOf=source.Id,Reason="Comparing the original recording with a second model."};
        document.SetPrimary(source.Primary);
        document.AttachProvenance(new(source.Provenance?.SessionId??source.Id,
            source.Provenance?.Primary??new(source.PrimaryModel,null),new(verifier,null),source.Provenance?.SessionStartedUtc));
        document.ManualFinalDraft=source.ManualFinalDraft??(source.OutputText!=source.Primary?source.OutputText:null);
        document.PreviousManualDraft=source.PreviousManualDraft;
        document.ManualDraftArchive=new(source.ManualDraftArchive);
        return document;
    }

    public static void Complete(ReviewDocument document,ReviewDocument source,SavedVerificationResult result)
    {
        if(document.Primary!=source.Primary||result.Primary!=source.Primary||result.PrimaryModel!=source.PrimaryModel||
            result.VerificationModel!=document.VerificationModel||string.IsNullOrWhiteSpace(result.ComparisonId)||
            source.Provenance?.Primary.Revision is {Length:>0} pinned&&result.PrimaryRevision!=pinned||
            result.Pieces==null||result.Pieces.Any(p=>p.Id.Length!=16||!p.Id.All(Uri.IsHexDigit))||
            result.Pieces.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=result.Pieces.Count||
            string.Concat(result.Pieces.Select(p=>p.Primary))!=source.Primary||
            string.Concat(result.Pieces.Select(p=>p.Verification))!=result.Verification)
            throw new InvalidDataException("The second transcript comparison does not match its original text and models.");
        var primary=source.Provenance?.Primary??new(source.PrimaryModel,result.PrimaryRevision);
        document.CompletePendingVerificationProvenance(new(source.Provenance?.SessionId??source.Id,primary,
            new(result.VerificationModel,result.VerificationRevision),source.Provenance?.SessionStartedUtc,result.ComparisonId));
        document.Verification=result.Verification;document.Pieces=new(result.Pieces);document.Choices.Clear();
        document.VerificationCompleted=true;
        document.Reason=document.UnresolvedCount>0?"The second transcript is ready. Compare the wording before inserting.":
            "Both transcripts agree. Insert at your selected cursor when ready.";
    }
}
