using Xunit;
using LocalDictation.App.Services;

namespace LocalDictation.Core.Tests;

public sealed class HistoryDestinationMemoryTests
{
    [Fact]
    public void LocalDictationWindowsDoNotReplaceTheExternalDestination()
    {
        var memory=new HistoryDestinationMemory(10);memory.Observe(42,20,"Editor");
        memory.Observe(99,10,"Review");Assert.Equal(new HistoryInsertionDestination(42,20),memory.Current);
    }
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void ShellNavigationPreservesThePreviouslyChosenEditor(string windowClass)
    {
        var memory=new HistoryDestinationMemory(10);memory.Observe(42,20,"Editor");
        memory.Observe(99,30,windowClass);Assert.Equal(new HistoryInsertionDestination(42,20),memory.Current);
    }
    [Fact]
    public void AnExplorerApplicationWindowIsStillAnEligibleDestination()
    {
        var memory=new HistoryDestinationMemory(10);memory.Observe(42,20,"Editor");
        memory.Observe(99,30,"CabinetWClass");Assert.Equal(new HistoryInsertionDestination(99,30),memory.Current);
    }
    [Fact]
    public void MissingWindowOrProcessDoesNotEraseTheChosenApplication()
    {
        var memory=new HistoryDestinationMemory(10);memory.Observe(42,20,"Editor");
        memory.Observe(0,30,"Editor");memory.Observe(99,0,"Editor");Assert.Equal(new HistoryInsertionDestination(42,20),memory.Current);
    }
    [Fact]
    public void CapturedCommandDestinationIsImmutableWhenAnotherAppBecomesForeground()
    {
        var memory=new HistoryDestinationMemory(10);memory.Observe(42,20,"Editor");var selected=memory.Current;
        memory.Observe(99,30,"Other editor");Assert.Equal(new HistoryInsertionDestination(42,20),selected);
        Assert.Equal(new HistoryInsertionDestination(99,30),memory.Current);
    }
}
