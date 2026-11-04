namespace Notification.Domain;

/// <summary>The event that caused an email. Stored and returned by its camelCase wire name.</summary>
public enum EventType
{
    UserRegistered,
    OperatorApproved,
    OperatorRejected,
    BookingConfirmed,
    BookingSeatsChanged,
    BookingCancelled,
    TicketReady,
    RefundCompleted,
    BookingFailed,
    BookingExpired,
}
