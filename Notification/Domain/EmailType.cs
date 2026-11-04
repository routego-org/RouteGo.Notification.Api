namespace Notification.Domain;

/// <summary>Which email Notification sends. One consumed event leads to exactly one email type.</summary>
public enum EmailType
{
    CustomerVerification,
    OperatorVerification,
    OperatorApproved,
    OperatorRejected,
    BookingConfirmed,
    NewBooking,
    TicketReady,
    TicketReissued,
    SeatsChanged,
    DepartureReminder,
    BookingCancelled,
    RefundCompleted,
    BookingFailed,
    BookingExpired,
}
