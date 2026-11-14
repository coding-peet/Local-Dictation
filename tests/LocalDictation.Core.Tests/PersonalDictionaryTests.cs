using LocalDictation.Core;
using Xunit;
public class PersonalDictionaryTests
{
    static PersonalDictionarySettings Config(params ExactCorrection[] rules)=>new([],rules);
    [Fact] public void LongestExactMatchesUseUnicodeBoundariesAndNeverRecurse()
    {
        var c=Config(new ExactCorrection("nexus bom","Nexus BOM"),new ExactCorrection("nexus","Nexus"),new ExactCorrection("abc","nexus"));
        var result=PersonalDictionary.Apply("nexus bom, nexus. nexusX x_nexus abc",c);
        Assert.Equal("Nexus BOM, Nexus. nexusX x_nexus nexus",result.Text);Assert.Equal(3,result.Applied.Count);
    }
    [Fact] public void AccentsAndCaseAreExactAndManualTextAlwaysWins()
    {
        var c=Config(new ExactCorrection("café","Cafe"));Assert.Equal("Cafe CAFÉ",PersonalDictionary.Apply("cafe\u0301 CAFÉ",c).Text);
        var d=new ReviewDocument{Dictionary=new(c,true)};d.SetPrimary("café");Assert.Equal("café",d.Primary);Assert.Equal("Cafe",d.OutputText);
        d.ManualFinalDraft="my wording";Assert.Equal("my wording",d.OutputText);
    }
    [Fact] public void DuplicateConflictingAndEmptyRulesAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>PersonalDictionary.Validate(Config(new ExactCorrection("same","one"),new ExactCorrection("same","two"))));
        Assert.Throws<ArgumentException>(()=>PersonalDictionary.Validate(Config(new ExactCorrection(" ","value"))));
        Assert.Throws<ArgumentException>(()=>PersonalDictionary.Validate(new([new string('x',1025)],[])));
    }
    [Fact] public void ProtectedSettingsRoundtripAndInvalidWritePreservesEarlierVersion()
    {
        var root=Path.Combine(Path.GetTempPath(),"dictionary-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new PersonalDictionaryStore(root);var c=new PersonalDictionarySettings(["private synthetic name"],[new ExactCorrection("nex","Nexus")]);store.Save(c);
            Assert.Equal(c.Hints,store.Load().Hints);var before=File.ReadAllBytes(Path.Combine(root,"personal-dictionary.settings"));
            Assert.DoesNotContain("private synthetic name",System.Text.Encoding.UTF8.GetString(before));
            Assert.Throws<ArgumentException>(()=>store.Save(Config(new ExactCorrection("","bad"))));Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"personal-dictionary.settings")));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
