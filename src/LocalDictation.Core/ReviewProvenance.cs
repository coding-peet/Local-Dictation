namespace LocalDictation.Core;

public sealed record PinnedModel(string Id,string? Revision);
public sealed record ReviewProvenance(string SessionId,PinnedModel Primary,PinnedModel? Verification,
    DateTimeOffset? SessionStartedUtc,string? ComparisonId=null);
