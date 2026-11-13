namespace LocalDictation.Core;

public static class DestinationDeliveryPolicy
{
    public static bool InsertsAfterFinish(string? processName, IEnumerable<string>? applications = null) =>
        string.Equals(processName, "olk", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "OUTLOOK", StringComparison.OrdinalIgnoreCase) ||
        processName is not null && applications?.Any(app => string.Equals(app, processName, StringComparison.OrdinalIgnoreCase)) == true;

    public static bool RememberLiveFailure(ICollection<string> applications, string? processName, bool terminalTransportFailure)
    {
        if (!terminalTransportFailure || string.IsNullOrWhiteSpace(processName) ||
            processName != processName.Trim() || processName.Length > 128 ||
            processName.Any(c => char.IsControl(c) || c is '\\' or '/' or ':') ||
            InsertsAfterFinish(processName, applications)) return false;
        applications.Add(processName);
        return true;
    }
}
