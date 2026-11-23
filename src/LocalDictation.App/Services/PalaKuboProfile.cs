using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LocalDictation.App.Services;

internal sealed record PalaCodexModel(string Id, string Label, string[] Efforts, bool IsDefault);
internal sealed record PalaNoteTranscript(int? Note, DateTimeOffset? Captured, string Text, bool OnNote, string[]? Decisions)
{
    public string Label
    {
        get
        {
            var note = Note is { } number ? $"Note #{number}" : "Note";
            if (Captured is { } timestamp) note += $" · {timestamp.ToLocalTime():g}";
            return OnNote ? note : $"{note} · retained by Bridge";
        }
    }
}
internal sealed record PalaTranscriptPage(PalaNoteTranscript[] Records, bool HasMore);

internal static class PalaKuboProfile
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task<PalaCodexModel[]> Models(CancellationToken token) =>
        Read<PalaCodexModel[]>(token, "--codex-models");

    public static Task<PalaTranscriptPage> Transcripts(int offset, CancellationToken token) =>
        Read<PalaTranscriptPage>(token, "--note-transcripts", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));

    static async Task<T> Read<T>(CancellationToken token, params string[] arguments)
    {
        var start = PalaBridgeInstallation.ManagedStart("bridge.py", false, arguments)
            ?? throw new IOException("The local Bridge runtime is unavailable.");
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = Process.Start(start) ?? throw new IOException("The local Bridge query could not start.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var response = await output;
            await errors;
            if (process.ExitCode != 0) throw new IOException("The local Bridge query failed.");
            return JsonSerializer.Deserialize<T>(response, Json) ?? throw new IOException("Bridge returned no data.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync();
            }
            throw;
        }
    }
}
