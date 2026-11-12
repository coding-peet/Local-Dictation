using System.Globalization;
using LocalDictation.Core;
using Xunit;

public class DraftRevisionTests
{
    [Theory]
    [InlineData("hello","hello world",5)]
    [InlineData("hello cat","hello car",8)]
    [InlineData("same","same",4)]
    [InlineData("","",0)]
    [InlineData("","text",0)]
    [InlineData("text","",0)]
    [InlineData("A😀","A😁",1)]
    [InlineData("cafe\u0301","cafe\u0300",3)]
    [InlineData("cafe","cafe\u0301",3)]
    [InlineData("cafe\u0301","cafe",3)]
    [InlineData("a\u0301b","a\u0301c",2)]
    [InlineData("prefix 👩‍💻","prefix 👩‍🔬",7)]
    [InlineData("👩","👩‍💻",0)]
    [InlineData("👍🏻","👍🏽",0)]
    [InlineData("🇸🇰","🇸🇮",0)]
    [InlineData("Line\r","Line\r\n",4)]
    [InlineData("café","cafe\u0301",3)]
    [InlineData("a\u0301","á",0)]
    [InlineData("A😀e\u0301","A😀e\u0301",5)]
    public void RetainsTheLongestOrdinalPrefixWithCompleteTextElements(string current,string next,int expected)
    {
        var boundary=DraftRevision.CommonPrefixBoundary(current,next);
        Assert.Equal(expected,boundary);
        Assert.Equal(current[..boundary],next[..boundary]);
        Assert.True(IsBoundary(current,boundary));
        Assert.True(IsBoundary(next,boundary));
    }

    [Fact]
    public void LongSharedPrefixKeepsAllCompleteElementsBeforeARevisedCombiningSequence()
    {
        var shared=new string('a',50_000);
        var current=shared+"e\u0301 before";
        var next=shared+"e\u0300 after";
        Assert.Equal(shared.Length,DraftRevision.CommonPrefixBoundary(current,next));
    }

    [Theory]
    [InlineData(null,"text","current")]
    [InlineData("text",null,"next")]
    public void RejectsNullInput(string? current,string? next,string parameter)
    {
        var error=Assert.Throws<ArgumentNullException>(()=>DraftRevision.CommonPrefixBoundary(current!,next!));
        Assert.Equal(parameter,error.ParamName);
    }

    static bool IsBoundary(string value,int offset)=>offset==value.Length||
        Array.BinarySearch(StringInfo.ParseCombiningCharacters(value),offset)>=0;
}
