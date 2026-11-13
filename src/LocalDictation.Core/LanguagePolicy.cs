using System.Globalization;

namespace LocalDictation.Core;

public sealed record SpeechLanguage(string Code)
{
    public string Label
    {
        get
        {
            try{return Code.ToUpperInvariant()+" · "+CultureInfo.GetCultureInfo(Code).EnglishName;}
            catch(CultureNotFoundException){return Code.ToUpperInvariant();}
        }
    }
    public override string ToString()=>Label;
}

public static class LanguagePolicy
{
    public static IReadOnlyList<SpeechLanguage> Available(IEnumerable<ModelInfo> models)=>models
        .Where(m=>m.Installed&&m.Compatible).SelectMany(m=>m.Languages).Where(l=>l!="auto")
        .Distinct(StringComparer.Ordinal).OrderBy(l=>l,StringComparer.Ordinal).Select(l=>new SpeechLanguage(l)).ToList();

    public static IReadOnlyList<SpeechLanguage> CorrectionLanguages(IEnumerable<ModelInfo> models,IReadOnlyList<string>? preferred)
    {
        var available=Available(models);
        if(preferred is not {Count:>0})return available;
        var supported=available.Select(l=>l.Code).ToHashSet(StringComparer.Ordinal);
        return preferred.Where(supported.Contains).Distinct(StringComparer.Ordinal).Select(l=>new SpeechLanguage(l)).ToList();
    }

    public static ModelInfo? RetryModel(IEnumerable<ModelInfo> models,string current,string language)=>models
        .Where(m=>m.Installed&&m.Compatible&&m.Languages.Contains(language))
        .OrderByDescending(m=>m.Id==current).ThenByDescending(m=>m.LiveSupported).ThenByDescending(m=>m.Id.EndsWith("-turbo",StringComparison.Ordinal)).ThenByDescending(m=>m.RuntimeTested)
        .ThenBy(m=>m.Id,StringComparer.Ordinal).FirstOrDefault();

    public static string Label(string requested,string? actual,string? mode,ModelInfo? model)
    {
        if(mode=="model_fixed"&&actual!=null)return actual.ToUpperInvariant();
        if(requested!="auto")return requested.ToUpperInvariant();
        if(mode=="detected"&&actual!=null)return "Auto · "+actual.ToUpperInvariant();
        var fixedLanguage=model?.Languages.Where(l=>l!="auto").ToArray();
        return fixedLanguage is {Length:1}?fixedLanguage[0].ToUpperInvariant():"Auto · …";
    }

    public static void ValidateForModel(ModelInfo model,string requested,IReadOnlyList<string> preferred)
    {
        ValidatePreferences(preferred);
        if(requested=="auto")
        {
            if(!model.Languages.Any(l=>l!="auto"&&(preferred.Count==0||preferred.Contains(l))))
                throw new ArgumentException("Keep at least one Auto language supported by "+model.Name+".");
        }
        else if(!model.Languages.Contains(requested))throw new ArgumentException("The selected model does not support this language.");
    }

    public static void ValidatePreferences(IReadOnlyList<string>? languages)
    {
        if(languages==null||languages.Count>100||languages.Distinct(StringComparer.Ordinal).Count()!=languages.Count||
            languages.Any(l=>l==null||l.Length is <2 or >3||l.Any(c=>c<'a'||c>'z')||l=="auto"))
            throw new ArgumentException("Choose distinct supported speech language codes, or clear all tags to use every supported language.");
    }
}
