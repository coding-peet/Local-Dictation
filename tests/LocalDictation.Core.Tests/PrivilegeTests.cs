using LocalDictation.Core;
using Xunit;

public class PrivilegeTests
{
    [Theory]
    [InlineData(0x2000,0x2000,true)]
    [InlineData(0x2000,0x3000,true)]
    [InlineData(0x3000,0x2000,false)]
    [InlineData(0x3000,0x3000,false)]
    public void ElevatedDestinationRemainsReviewOnlyEvenIfClientIsElevated(int target,int client,bool expected)
    {
        Assert.Equal(expected,TargetPrivileges.CanInsert(target,client));
    }
}
