using LocalDictation.Core;
using Xunit;

public sealed class LanguagePolicyTests
{
    static ModelInfo Model(string id,params string[] languages)=>new(){Id=id,Name=id,Installed=true,Compatible=true,RuntimeTested=true,LiveSupported=true,Languages=languages};
    [Fact] public void PickerUsesInstalledCapabilitiesAndNeverOffersAutoAsASpeechLanguage()
    {
        var models=new[]{Model("sk-only","sk"),Model("multi","auto","sk","en","es","cs"),Model("absent","de") with{Installed=false}};
        Assert.Equal(new[]{"cs","en","es","sk"},LanguagePolicy.Available(models).Select(l=>l.Code));
    }
    [Fact] public void CorrectionPickerOnlyOffersPreferredInstalledLanguagesInTheirChosenOrder()
    {
        var models=new[]{Model("multi","auto","sk","en","es","cs")};
        Assert.Equal(new[]{"sk","en"},LanguagePolicy.CorrectionLanguages(models,["sk","en"]).Select(l=>l.Code));
        Assert.Empty(LanguagePolicy.CorrectionLanguages(models,["de"]));
    }
    [Fact] public void WithoutPreferencesPickerAndAutoCoverAllSupportedLanguages()
    {
        var primary=Model("multi","auto","sk","en","es");
        Assert.Equal(new[]{"en","es","sk"},LanguagePolicy.CorrectionLanguages([primary],[]).Select(l=>l.Code));
        LanguagePolicy.ValidatePreferences([]);
        LanguagePolicy.ValidateForModel(primary,"auto",[]);
        Assert.True(ModelEligibility.CanVerify(Model("verifier","en","es","sk"),"auto",primary,[]));
        Assert.False(ModelEligibility.CanVerify(Model("narrow","en"),"auto",primary,[]));
    }
    [Fact] public void WireRequestUsesNullInsteadOfAnInvalidEmptyAllowlist()
    {
        var settings=new Settings{Language="auto",PreferredLanguages=[]};
        using var empty=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(Protocol.SessionStart(settings),Protocol.Json));
        Assert.Equal(System.Text.Json.JsonValueKind.Null,empty.RootElement.GetProperty("preferred_languages").ValueKind);
        settings.PreferredLanguages=["sk","en"];
        using var constrained=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(Protocol.SessionStart(settings),Protocol.Json));
        Assert.Equal(new[]{"sk","en"},constrained.RootElement.GetProperty("preferred_languages").EnumerateArray().Select(v=>v.GetString()));
        Assert.Equal("auto",constrained.RootElement.GetProperty("language").GetString());
    }
    [Fact] public void AFixedModelDoesNotPretendToHaveDetectedTheLanguage()
    {
        Assert.Equal("SK",LanguagePolicy.Label("auto",null,null,Model("sk-only","sk")));
        Assert.Equal("Auto · …",LanguagePolicy.Label("auto",null,null,Model("multi","auto","sk","en")));
        Assert.Equal("Auto · EN",LanguagePolicy.Label("auto","en","detected",null));
        Assert.Equal("ES",LanguagePolicy.Label("es","es","forced",null));
    }
    [Fact] public void RetryKeepsCompatibleModelAndExplicitlyNamesAnyRequiredSwitch()
    {
        var sk=Model("sk-only","sk");var multi=Model("multi","auto","sk","en","es");
        Assert.Equal(sk,LanguagePolicy.RetryModel(new[]{sk,multi},sk.Id,"sk"));
        Assert.Equal(multi,LanguagePolicy.RetryModel(new[]{sk,multi},sk.Id,"es"));
        Assert.Null(LanguagePolicy.RetryModel(new[]{sk},sk.Id,"es"));
    }
    [Fact] public void InvalidAutoPreferencesAreRejectedAndDefaultsExcludeUnspokenLanguages()
    {
        LanguagePolicy.ValidatePreferences([]);
        Assert.Throws<ArgumentException>(()=>LanguagePolicy.ValidatePreferences(["auto"]));
        Assert.Throws<ArgumentException>(()=>LanguagePolicy.ValidatePreferences(["en","en"]));
        var settings=new Settings();
        Assert.Contains("sk",settings.PreferredLanguages);Assert.Contains("en",settings.PreferredLanguages);
        Assert.DoesNotContain("ru",settings.PreferredLanguages);Assert.DoesNotContain("sr",settings.PreferredLanguages);Assert.DoesNotContain("cs",settings.PreferredLanguages);
    }
    [Fact] public void RemovingLastSupportedAutoLanguageCannotBecomeAValidConfiguration()
    {
        var slovak=Model("Slovak","sk");
        LanguagePolicy.ValidateForModel(slovak,"auto",["sk","en"]);
        Assert.Throws<ArgumentException>(()=>LanguagePolicy.ValidateForModel(slovak,"auto",["en"]));
        LanguagePolicy.ValidateForModel(slovak,"sk",["en"]);
    }
    [Fact] public void VerificationCoversEveryEffectivePreferredCandidate()
    {
        var primary=Model("primary","auto","sk","en","es");
        var english=Model("english","en");var narrow=Model("narrow","auto","sk","en");
        Assert.True(ModelEligibility.CanVerify(english,"auto",primary,["en"]));
        Assert.False(ModelEligibility.CanVerify(narrow,"auto",primary,["en","es"]));
        Assert.False(ModelEligibility.CanVerify(narrow,"auto",primary,["de"]));
    }
    [Fact] public void ReplayParsesSavedWaveWithoutMutatingOrInventingAudio()
    {
        var wave=new byte[48];"RIFF"u8.CopyTo(wave);BitConverter.GetBytes(40).CopyTo(wave,4);"WAVEfmt "u8.CopyTo(wave.AsSpan(8));
        BitConverter.GetBytes(16).CopyTo(wave,16);BitConverter.GetBytes((short)1).CopyTo(wave,20);BitConverter.GetBytes((short)1).CopyTo(wave,22);
        BitConverter.GetBytes(16000).CopyTo(wave,24);BitConverter.GetBytes(32000).CopyTo(wave,28);BitConverter.GetBytes((short)2).CopyTo(wave,32);BitConverter.GetBytes((short)16).CopyTo(wave,34);
        "data"u8.CopyTo(wave.AsSpan(36));BitConverter.GetBytes(4).CopyTo(wave,40);new byte[]{1,2,3,4}.CopyTo(wave,44);
        Assert.Equal(new byte[]{1,2,3,4},RecordedAudio.PcmFromWave(wave));
        wave[24]=0;Assert.Throws<InvalidDataException>(()=>RecordedAudio.PcmFromWave(wave));
    }
}
