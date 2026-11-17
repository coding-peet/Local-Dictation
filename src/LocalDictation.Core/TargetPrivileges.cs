namespace LocalDictation.Core;

public static class TargetPrivileges
{
    // Windows mandatory integrity: High (0x3000) and above remain Review-only.
    public static bool CanInsert(int targetIntegrity,int clientIntegrity) =>
        targetIntegrity<0x3000 && targetIntegrity<=clientIntegrity;
}
