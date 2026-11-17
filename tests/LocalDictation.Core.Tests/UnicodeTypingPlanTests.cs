using LocalDictation.Core;
using Xunit;

public class UnicodeTypingPlanTests
{
    [Fact]
    public void PairsEachLiteralUtf16UnitWithDownThenUpInOriginalOrder()
    {
        Assert.Equal(new[]{
            new UnicodeStroke(0x0041,false),new UnicodeStroke(0x0041,true),
            new UnicodeStroke(0x013e,false),new UnicodeStroke(0x013e,true),
            new UnicodeStroke(0x00a0,false),new UnicodeStroke(0x00a0,true),
            new UnicodeStroke(0x00e9,false),new UnicodeStroke(0x00e9,true)
        },UnicodeTypingPlan.Create("Aľ\u00a0é"));
    }

    [Fact]
    public void EmptyTextProducesNoStrokes()
    {
        Assert.Empty(UnicodeTypingPlan.Create(""));
    }

    [Fact]
    public void NullTextIsRejectedBeforeProducingAPlan()
    {
        var error=Assert.Throws<ArgumentNullException>(()=>UnicodeTypingPlan.Create(null!));
        Assert.Equal("text",error.ParamName);
    }

    [Theory]
    [InlineData("Príliš žltý kôň úpenlivo erdží. Ľščťžýáíéôäňŕĺ")]
    [InlineData("  leading\u00a0and trailing  ")]
    [InlineData("e\u0301 a\u0308\u0301")]
    [InlineData("👩‍💻 👍🏽 🇸🇰 👨‍👩‍👧‍👦")]
    [InlineData("&<>\"'\\/@+?!")]
    [InlineData("Á\u200b\u2060\ufeffZ")]
    public void ValidTextRoundTripsWithoutNormalizationOrGraphemeLoss(string text)
    {
        var plan=UnicodeTypingPlan.Create(text);
        Assert.Equal(text.Length*2,plan.Length);
        Assert.Equal(text,new string(plan.Where(stroke=>!stroke.KeyUp).Select(stroke=>(char)stroke.Scan).ToArray()));
        for(var i=0;i<text.Length;i++)
        {
            Assert.Equal(new UnicodeStroke(text[i],false),plan[2*i]);
            Assert.Equal(new UnicodeStroke(text[i],true),plan[2*i+1]);
        }
    }

    [Fact]
    public void ZwjEmojiPreservesBothSurrogatePairsAndTheJoinerAsLiteralUnits()
    {
        var plan=UnicodeTypingPlan.Create("👩‍💻");
        Assert.Equal(new ushort[]{0xd83d,0xd83d,0xdc69,0xdc69,0x200d,0x200d,0xd83d,0xd83d,0xdcbb,0xdcbb},
            plan.Select(stroke=>stroke.Scan));
        Assert.Equal(new[]{false,true,false,true,false,true,false,true,false,true},plan.Select(stroke=>stroke.KeyUp));
    }

    [Fact]
    public void CombiningAndPrecomposedTextKeepDifferentOriginalCodeUnits()
    {
        var combining=UnicodeTypingPlan.Create("e\u0301");
        var precomposed=UnicodeTypingPlan.Create("é");
        Assert.Equal(new ushort[]{0x0065,0x0065,0x0301,0x0301},combining.Select(stroke=>stroke.Scan));
        Assert.Equal(new ushort[]{0x00e9,0x00e9},precomposed.Select(stroke=>stroke.Scan));
    }

    [Theory]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    public void RejectsUnicodeLineAndParagraphSeparatorsAtAnyPayloadPosition(char separator)
    {
        foreach(var text in new[]{separator+"Text","Before"+separator+"After","Text"+separator})
        {
            var error=Assert.Throws<ArgumentException>(()=>UnicodeTypingPlan.Create(text));
            Assert.Equal("text",error.ParamName);
        }
    }

    public static IEnumerable<object[]> ControlCharacters()
    {
        foreach(var code in Enumerable.Range(0,32).Concat(Enumerable.Range(0x7f,33)))
            yield return new object[]{(char)code};
    }

    [Theory]
    [MemberData(nameof(ControlCharacters))]
    public void RejectsEveryC0AndC1ControlAtAnyPayloadPosition(char control)
    {
        foreach(var text in new[]{control+"Text","Before"+control+"After","Text"+control})
        {
            var error=Assert.Throws<ArgumentException>(()=>UnicodeTypingPlan.Create(text));
            Assert.Equal("text",error.ParamName);
        }
    }

    [Fact]
    public void RejectsUnpairedSurrogatesWithoutRepairOrReplacement()
    {
        foreach(var text in new[]{"\ud800","\udfff","\ud800A","A\udfff","\ud800\ud800","\udfff\udfff",
            "\udfff\ud800","\ud83d\udc69\ud800","\udfff\ud83d\udc69","Before\ud800After"})
        {
            var error=Assert.Throws<ArgumentException>(()=>UnicodeTypingPlan.Create(text));
            Assert.Equal("text",error.ParamName);
        }
    }

    [Fact]
    public void TenThousandUtf16UnitsHaveExactlyTwentyThousandPairedStrokes()
    {
        var text=string.Concat(Enumerable.Repeat("Ľúbimčaj. ",1000));
        Assert.Equal(10_000,text.Length);
        var plan=UnicodeTypingPlan.Create(text);
        Assert.Equal(20_000,plan.Length);
        for(var i=0;i<text.Length;i++)
        {
            Assert.Equal(new UnicodeStroke(text[i],false),plan[2*i]);
            Assert.Equal(new UnicodeStroke(text[i],true),plan[2*i+1]);
        }
    }
}
