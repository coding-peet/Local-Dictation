namespace LocalDictation.Core;

public static class DestinationDeliveryPolicy
{
    public static bool InsertsAfterFinish(string? processName)=>
        string.Equals(processName,"olk",StringComparison.OrdinalIgnoreCase)||
        string.Equals(processName,"OUTLOOK",StringComparison.OrdinalIgnoreCase);
}
