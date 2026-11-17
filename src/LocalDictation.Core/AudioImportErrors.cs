using System.Net.WebSockets;

namespace LocalDictation.Core;

public enum AudioImportPhase { Preparation, Transcription, Saving }

/// <summary>Curated import copy. Raw exception and API body text never become row messages.</summary>
public static class AudioImportErrors
{
    public const string EmptyFile="This audio file is empty. Choose a file that contains audio.";
    public const string OversizeFile="This audio file is too large. Choose a file below 26 MiB.";
    public const string ChangedFile="The selected file changed after selection. Select it again to import the current version.";
    public const string MissingFile="The selected file is no longer available. Restore it or select another file.";
    public const string UnreadableFile="This audio file could not be read. Check its permissions and whether another app has locked it.";
    public const string UnsupportedAudio="This audio file could not be decoded. Use a supported WAV, MP3, FLAC, M4A or OGG/Opus file.";
    public const string NoAudio="This file contains no audio. Choose a file with an audio track and samples.";
    public const string DurationLimit="Decoded audio exceeds the local API duration limit. Choose a shorter file; the complete original is preserved.";
    public const string ApiUnavailable="The local API is unavailable. Check its connection and readiness, then retry this file.";
    public const string ApiBusy="The local API is busy or preparing. Wait until it is ready, then retry this file.";
    public const string ApiTimeout="The local API did not respond in time. Wait until it is idle, then retry this file.";
    public const string ApiRejected="The local API refused the request. Check local API access, then retry this file.";
    public const string ApiProcessingFailed="The local API could not prepare this file. Check the API status, then retry this file.";
    public const string PreparationFailed="Audio preparation failed. Retry this file; the original is preserved.";
    public const string InvalidResponse="The local API returned invalid audio. Check the API and retry this file.";
    public const string TranscriptionFailed="Transcription did not finish. Review any received text before retrying this file.";
    public const string SavingFailed="The import result could not be fully saved. Keep the app open and save the text from Review before exiting.";

    internal static void CheckEncodedLength(long length)
    {
        if(length<=0)throw new AudioImportException(EmptyFile);
        if(length>AudioImportSource.MaxEncodedBytes)throw new AudioImportException(OversizeFile);
    }

    public static string Message(Exception error,AudioImportPhase phase)
    {
        // A save warning must remain visible even when a preceding phase also failed.
        if(phase==AudioImportPhase.Saving)return SavingFailed;
        // AudioImportException is reserved for curated client copy, including the
        // existing dynamic API duration limit and old-API compatibility guidance.
        if(error is AudioImportException)return error.Message;
        if(error is TimeoutException or OperationCanceledException)return ApiTimeout;
        if(error is HttpRequestException or WebSocketException)return ApiUnavailable;
        if(phase==AudioImportPhase.Transcription)return TranscriptionFailed;
        return error switch
        {
            FileNotFoundException or DirectoryNotFoundException=>MissingFile,
            UnauthorizedAccessException or IOException=>UnreadableFile,
            InvalidDataException=>UnsupportedAudio,
            _=>PreparationFailed
        };
    }
}
