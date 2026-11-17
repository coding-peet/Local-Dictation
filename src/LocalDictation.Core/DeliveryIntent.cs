namespace LocalDictation.Core;

/// <summary>Final acknowledgement and permission to restore focus are independent.</summary>
public readonly record struct DeliveryIntent(bool IsFinal, bool RestoreFocus)
{
    public static DeliveryIntent Live => new(false, false);
    public static DeliveryIntent AutomaticFinal => new(true, false);
    public static DeliveryIntent ExplicitFinal => new(true, true);
}
