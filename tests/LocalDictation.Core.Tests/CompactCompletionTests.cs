using LocalDictation.Core;
using Xunit;

public class CompactCompletionTests
{
    [Theory]
    [InlineData(true,true,false,false,0,CompactCompletionAction.Insert)]
    [InlineData(true,true,true,true,0,CompactCompletionAction.Insert)]
    [InlineData(true,true,true,true,2,CompactCompletionAction.ReviewDifferences)]
    [InlineData(true,true,false,false,2,CompactCompletionAction.ReviewDifferences)]
    [InlineData(true,true,true,false,0,CompactCompletionAction.KeepPending)]
    [InlineData(false,true,true,true,0,CompactCompletionAction.KeepPending)]
    [InlineData(false,true,false,false,0,CompactCompletionAction.KeepPending)]
    [InlineData(true,false,false,false,0,CompactCompletionAction.KeepPending)]
    [InlineData(false,false,true,false,2,CompactCompletionAction.KeepPending)]
    public void AutomaticPlacementRequiresCompletedPrimaryAndRequestedVerification(
        bool normal,bool final,bool requested,bool verified,int unresolved,CompactCompletionAction expected)
    {
        Assert.Equal(expected,CompactCompletion.Decide(normal,final,requested,verified,unresolved));
    }
}
