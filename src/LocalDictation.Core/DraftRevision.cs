using System.Globalization;

namespace LocalDictation.Core;

public static class DraftRevision
{
    /// <summary>Returns the longest ordinal common prefix ending on a text-element boundary in both strings, as a UTF-16 offset.</summary>
    public static int CommonPrefixBoundary(string current,string next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        var common=0;
        while(common<current.Length&&common<next.Length)
        {
            var currentLength=StringInfo.GetNextTextElementLength(current.AsSpan(common));
            var nextLength=StringInfo.GetNextTextElementLength(next.AsSpan(common));
            if(currentLength!=nextLength||
                !current.AsSpan(common,currentLength).SequenceEqual(next.AsSpan(common,nextLength)))break;
            common+=currentLength;
        }
        return common;
    }
}
