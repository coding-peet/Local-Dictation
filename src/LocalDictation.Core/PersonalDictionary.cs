using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace LocalDictation.Core;

public sealed record ExactCorrection(string Source,string Replacement);
public sealed record PersonalDictionarySettings(IReadOnlyList<string> Hints,IReadOnlyList<ExactCorrection> Corrections);
public sealed record PersonalDictionarySnapshot(PersonalDictionarySettings Settings,bool CorrectionsEnabled);
public sealed record DictionaryCorrectionResult(string Text,IReadOnlyList<string> Applied);

public static class PersonalDictionary
{
    public static void Validate(PersonalDictionarySettings settings)
    {
        if(settings.Hints==null||settings.Corrections==null)throw new ArgumentException("Dictionary lists are required.");
        if(settings.Hints.Count>100||string.Join(", ",settings.Hints).Length>1024||settings.Hints.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Use up to 100 nonempty hint terms and 1024 characters in total. The model may require a shorter list.");
        if(settings.Corrections.Count>100)throw new ArgumentException("Use up to 100 exact corrections.");
        var sources=new HashSet<string>(StringComparer.Ordinal);
        foreach(var rule in settings.Corrections)
        {
            if(rule==null||string.IsNullOrWhiteSpace(rule.Source)||string.IsNullOrWhiteSpace(rule.Replacement)||rule.Source.Length>128||rule.Replacement.Length>512)throw new ArgumentException("Every correction needs a source (up to 128 characters) and replacement (up to 512).");
            if(!sources.Add(rule.Source.Normalize(NormalizationForm.FormC)))throw new ArgumentException("A correction source is repeated. Keep one exact replacement per source.");
        }
    }
    public static string Version(PersonalDictionarySettings settings)
    {Validate(settings);return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(settings))).ToLowerInvariant();}
    public static DictionaryCorrectionResult Apply(string text,PersonalDictionarySettings settings)
    {
        Validate(settings);if(settings.Corrections.Count==0)return new(text,[]);
        var rules=settings.Corrections.ToDictionary(r=>r.Source.Normalize(NormalizationForm.FormC),r=>r.Replacement,StringComparer.Ordinal);
        var alternatives=string.Join("|",rules.Keys.OrderByDescending(s=>s.Length).Select(Regex.Escape));
        var applied=new List<string>();
        var value=Regex.Replace(text.Normalize(NormalizationForm.FormC),@"(?<![\p{L}\p{N}\p{M}\p{Pc}])(?:"+alternatives+@")(?![\p{L}\p{N}\p{M}\p{Pc}])",
            m=>{applied.Add(m.Value);return rules[m.Value];},RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(250));
        return new(applied.Count==0?text:value,applied);
    }
}

/// <summary>Personal spellings and corrections are current-user protected, outside plain settings JSON.</summary>
public sealed class PersonalDictionaryStore(string directory)
{
    string FilePath=>Path.Combine(directory,"personal-dictionary.settings");
    public PersonalDictionarySettings Load()
    {
        if(!File.Exists(FilePath))return new([],[]);
        var data=UserProtection.Transform(File.ReadAllBytes(FilePath),true);
        try{var settings=JsonSerializer.Deserialize<PersonalDictionarySettings>(data)??throw new InvalidDataException("Invalid dictionary settings.");PersonalDictionary.Validate(settings);return settings;}
        finally{CryptographicOperations.ZeroMemory(data);}
    }
    public void Save(PersonalDictionarySettings settings)
    {
        PersonalDictionary.Validate(settings);var data=JsonSerializer.SerializeToUtf8Bytes(settings);
        try{AtomicFile.Write(FilePath,UserProtection.Transform(data,false));}finally{CryptographicOperations.ZeroMemory(data);}
    }
}
