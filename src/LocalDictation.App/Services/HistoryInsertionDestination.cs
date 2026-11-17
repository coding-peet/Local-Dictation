namespace LocalDictation.App.Services;

/// <summary>Memory-only identity of the last external foreground window.</summary>
public sealed record HistoryInsertionDestination(nint Window,int ProcessId);
