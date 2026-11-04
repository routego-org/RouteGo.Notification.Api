namespace Notification.Domain;

/// <summary>Why a send log entry was skipped. Present exactly when its status is skipped.</summary>
public enum SkipReason
{
    /// <summary>The recipient is on the do-not-send list.</summary>
    SuppressedAddress,

    /// <summary>The booking is known to be cancelled.</summary>
    BookingCancelled,

    /// <summary>The trip had already departed.</summary>
    DeparturePassed,
}
