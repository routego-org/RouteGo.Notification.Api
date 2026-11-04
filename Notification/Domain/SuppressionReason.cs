namespace Notification.Domain;

/// <summary>Why an address is on the do-not-send list.</summary>
public enum SuppressionReason
{
    /// <summary>The email service could never deliver to the address.</summary>
    PermanentBounce,

    /// <summary>The address's owner reported a RouteGo email as spam.</summary>
    Complaint,
}
