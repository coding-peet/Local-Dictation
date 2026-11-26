using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace LocalDictation.Core;

public static class AppPaths
{
    public static string Root => Environment.GetEnvironmentVariable("LOCAL_DICTATION_HOME") ?? @"C:\AI\LocalDictation";
    public static string Data => Path.Combine(Root, "data");
}

public static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class ReviewStore(string directory)
{
    public List<string> Errors { get; } = [];
    string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid Review ID.");
        return Path.Combine(directory, id + ".review");
    }

    public void Save(ReviewDocument document)
    {
        PrivateDictationPolicy.RequirePersistent(document);
        var plain = JsonSerializer.SerializeToUtf8Bytes(document);
        try { AtomicFile.Write(PathFor(document.Id), UserProtection.Transform(plain, false)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public IReadOnlyList<ReviewDocument> Load()
    {
        Errors.Clear();
        if (!Directory.Exists(directory)) return [];
        var result = new List<ReviewDocument>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.review"))
        {
            try
            {
                var plain = UserProtection.Transform(File.ReadAllBytes(path), true);
                try
                {
                    var doc = JsonSerializer.Deserialize<ReviewDocument>(plain) ?? throw new InvalidDataException();
                    if (PathFor(doc.Id) != path) throw new InvalidDataException();
                    result.Add(doc);
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            catch (Exception ex) when (ex is Win32Exception or JsonException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { Errors.Add(Path.GetFileName(path)); }
        }
        return result.OrderByDescending(d => d.Created).ToList();
    }

    public void Delete(string id) => File.Delete(PathFor(id));
}

static class UserProtection
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptProtectData(ref Blob input, string description, nint entropy, nint reserved,
        nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved,
        nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint memory);

    public static byte[] Transform(byte[] bytes, bool decrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = decrypt
                ? CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output)
                : CryptProtectData(ref input, "Local Dictation pending Review", 0, 0, 0, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0) LocalFree(output.Data);
        }
    }
}

public sealed class Settings
{
    public string Endpoint { get; set; } = "http://127.0.0.1:48125";
    public string? MicrophoneId { get; set; }
    public string PrimaryModel { get; set; } = "kinit/whisper-large-v3-turbo-sk";
    public string? VerificationModel { get; set; }
    public const string DefaultPalaNoteName = "Note";
    public string PalaBridgeName { get; set; } = DefaultPalaNoteName;
    public string PalaWeatherLocation { get; set; } = "";
    public bool PalaIntegrationEnabled { get; set; } = true;
    public bool PalaBridgeAutoStart { get; set; }
    // Null preserves the Bridge profile. An empty string follows the main Dictation model.
    public string? PalaBridgeModel { get; set; }
    public string? PalaKuboModel { get; set; }
    public string PalaKuboReasoningEffort { get; set; } = "high";
    public static IReadOnlyList<string> KuboReasoningEfforts { get; } =
        Array.AsReadOnly(new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" });
    public bool VerifyAfterRecording { get; set; }
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<AfterRecordingAction>))]
    public AfterRecordingAction AfterRecording { get; set; } = AfterRecordingAction.InsertDirectly;
    public bool InsertAtCursor { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanInsertLive => AfterRecording==AfterRecordingAction.InsertDirectly&&Live&&InsertAtCursor;
    public bool Live { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public string VocabularyPrompt {get;set;}="";
    public List<string> FinalOnlyApplications { get; set; } = [];
    public string Language { get; set; } = "sk";
    public List<string> PreferredLanguages { get; set; } = ["sk","en"];
    public bool EnableShortcut { get; set; } = true;
    public string Shortcut {get;set;}="Ctrl+Shift";
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ShortcutMode>))]
    public ShortcutMode ShortcutMode {get;set;}=ShortcutMode.Toggle;
    public string Theme {get;set;}="System";
    public int AudioRetentionDays {get;set;}=7;

    public Uri ServerUri()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != "http" || uri.Host is not ("127.0.0.1" or "localhost") ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query))
            throw new ArgumentException("Use a loopback HTTP address, for example http://127.0.0.1:48125.");
        return uri;
    }

    public static Settings Load(string? directory = null)
    {
        var file = Path.Combine(directory ?? AppPaths.Data, "settings.json");
        var settings = File.Exists(file) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) ?? new() : new();
        if (!IsPalaNoteName(settings.PalaBridgeName)) settings.PalaBridgeName = DefaultPalaNoteName;
        try { settings.PalaWeatherLocation = NormalizePalaWeatherLocation(settings.PalaWeatherLocation); }
        catch (ArgumentException) { settings.PalaWeatherLocation = ""; }
        return settings;
    }

    public static string NormalizePalaWeatherLocation(string? location)
    {
        var value = location?.Trim() ?? "";
        if (value.Length == 0) return "";
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (value.Any(char.IsControl) || parts[0].Length < 2 || parts.Length > 2 ||
            (parts.Length == 2 && (parts[1].Length != 2 || !parts[1].All(char.IsAsciiLetter))))
            throw new ArgumentException("Enter a town, optionally followed by a two-letter country code, such as Bratislava, SK.");
        var normalized = parts.Length == 1 ? parts[0] : $"{parts[0]}, {parts[1].ToUpperInvariant()}";
        if (normalized.Length > 100)
            throw new ArgumentException("Enter a weather location of 100 characters or fewer.");
        return normalized;
    }

    public static bool IsPalaNoteName(string? name) =>
        name is { Length: >= 1 and <= 10 } &&
        char.IsAsciiLetterOrDigit(name[0]) && name[^1] != ' ' &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is ' ' or '-');

    public static string ValidatePalaBridgeName(string name)
    {
        if (!IsPalaNoteName(name))
            throw new ArgumentException("Enter a name of 1 to 10 characters using A–Z, numbers, spaces or hyphens.");
        return name;
    }

    public void Save(string? directory = null)
    {
        ServerUri();
        ValidatePalaBridgeName(PalaBridgeName);
        PalaWeatherLocation = NormalizePalaWeatherLocation(PalaWeatherLocation);
        if (PalaBridgeModel is { Length: > 0 } model &&
            (model.Length > 256 || model.Any(char.IsWhiteSpace) || model.Any(char.IsControl)))
            throw new ArgumentException("Choose a valid Bridge model.");
        if (PalaKuboModel is { } kuboModel &&
            (kuboModel.Length is < 1 or > 128 || !char.IsAsciiLetterOrDigit(kuboModel[0]) ||
             kuboModel.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '/' or '-'))))
            throw new ArgumentException("Choose a valid Kubo processing model.");
        if (!KuboReasoningEfforts.Contains(PalaKuboReasoningEffort))
            throw new ArgumentException("Choose a valid Kubo reasoning effort.");
        if(!Enum.IsDefined(AfterRecording))throw new ArgumentException("Choose Insert directly or Preview first.");
        if(!Enum.IsDefined(ShortcutMode))throw new ArgumentException("Choose Toggle or Hold to record.");
        if(AudioRetentionDays is <0 or >3650)throw new ArgumentException("Choose 0 (Forever) or 1 to 3650 days for recordings.");
        ShortcutSpec.Parse(Shortcut);
        LanguagePolicy.ValidatePreferences(PreferredLanguages);
        if(Theme is not ("System" or "Light" or "Dark"))throw new ArgumentException("Choose System, Light, or Dark.");
        AtomicFile.Write(Path.Combine(directory ?? AppPaths.Data, "settings.json"),
            JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
