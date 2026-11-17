using System.Text.Json;
namespace LocalDictation.Core;

/// <summary>Consumes the existing session protocol without a destination, clipboard or application window.</summary>
public sealed class ImportedTranscript(ReviewDocument document,long expectedPcmBytes=0,long? serverMaxPcmBytes=null)
{
    bool final,failed;
    public bool Completed{get;private set;}
    public static void ValidateDuration(long pcmBytes,int serverMaxSeconds)
    {
        if(serverMaxSeconds is <1 or >3600)throw new InvalidDataException("The local API returned an invalid recording limit.");
        if(pcmBytes<=0||pcmBytes>Math.Min(600,serverMaxSeconds)*32000L)throw new AudioImportException($"This API accepts at most {Math.Min(600,serverMaxSeconds)} seconds per file. The complete original was preserved.");
    }
    public void Observe(JsonElement e)
    {
        string? Text(string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
        switch(Text("type"))
        {
            case "recording.limit_reached":
                var full=expectedPcmBytes>0&&(e.TryGetProperty("audio_received_seconds",out var duration)&&duration.TryGetDouble(out var seconds)
                    ?Math.Round(seconds*32000)==expectedPcmBytes:serverMaxPcmBytes==expectedPcmBytes);
                if(!full)failed=true;break;
            case "session.started":
                if(e.TryGetProperty("max_seconds",out var cap))
                {
                    ValidateDuration(expectedPcmBytes,cap.GetInt32());serverMaxPcmBytes=cap.GetInt32()*32000L;
                }
                var time=DateTimeOffset.TryParse(Text("session_started_utc"),out var started)?started:(DateTimeOffset?)null;
                document.AttachProvenance(new(Text("session_id")??throw new InvalidDataException("No session identity."),
                    new(Text("model")??"",Text("model_revision")),document.VerificationModel is {} verifier?new(verifier,Text("verification_revision")):null,time));break;
            case "transcript.updated":case "transcript.final":
                var value=Text("text")??"";
                if(value.Length>0||document.Primary.Length==0)document.SetPrimary(value);else if(Text("type")=="transcript.final")failed=true;
                document.Language=Text("language")??document.Language;document.LanguageMode=Text("language_mode")??document.LanguageMode;
                if(Text("type")=="transcript.final")final=true;break;
            case "verification.completed":
                document.Verification=Text("verification")??"";document.VerificationCompleted=true;
                document.Pieces=e.GetProperty("pieces").Deserialize<List<ReviewPiece>>(Protocol.Json)??[];
                if(document.Provenance is {} p)document.AttachProvenance(p with {ComparisonId=Text("comparison_id")});break;
            case "verification.failed":case "verification.cancelled":case "session.error":case "session.cancelled":failed=true;break;
            case "session.completed":Completed=final&&!failed;break;
        }
    }
}
