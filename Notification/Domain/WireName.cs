namespace Notification.Domain;

/// <summary>
/// The camelCase name an enumeration value carries in the database and on the wire
/// (<c>CustomerVerification</c> becomes <c>customerVerification</c>), so both spell it the way the
/// data model and the contract list it.
/// </summary>
public static class WireName
{
    /// <summary>The camelCase wire name of <paramref name="value"/>.</summary>
    public static string Of(Enum value)
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
