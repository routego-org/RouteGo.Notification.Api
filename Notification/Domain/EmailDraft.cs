namespace Notification.Domain;

/// <summary>
/// What an email slice decided for one email before anything is recorded: which email it is, who it
/// goes to and the finished message, or the reason it can never be sent as it is.
/// </summary>
public sealed record EmailDraft
{
    /// <summary>Which email this is.</summary>
    public required EmailType EmailType { get; init; }

    /// <summary>The event that caused it, as stored in the send log.</summary>
    public required EventType EventType { get; init; }

    /// <summary>What makes this the same email when its event arrives again: the event ID unless the email names a business key.</summary>
    public required string RepeatKey { get; init; }

    /// <summary>The booking the email is about, when the event names a usable one.</summary>
    public Guid? BookingId { get; init; }

    /// <summary>The trip's departure time, for an email about a trip.</summary>
    public DateTimeOffset? DepartsAt { get; init; }

    /// <summary>The recipient; null only when no recipient was known.</summary>
    public string? ToAddress { get; init; }

    /// <summary>The finished email; null when it could not be built.</summary>
    public EmailMessage? Message { get; init; }

    /// <summary>Why the email can never be sent as it is; set exactly when <see cref="Message"/> is null.</summary>
    public FailureReason? FailureReason { get; init; }

    /// <summary>The missing field or other human-readable detail of the failure.</summary>
    public string? FailureDetail { get; init; }

    /// <summary>
    /// Whether a booking already known to be cancelled stops this email. True for every booking
    /// email except the cancellation and refund emails, which are always sent.
    /// </summary>
    public bool IsStoppedByCancelledBooking { get; init; }

    /// <summary>Whether a departure time already passed stops this email. True only for the departure reminder.</summary>
    public bool IsStoppedByDeparture { get; init; }

    /// <summary>This draft, failed for <paramref name="reason"/> because of <paramref name="detail"/>.</summary>
    public EmailDraft Fail(FailureReason reason, string detail)
    {
        return this with { FailureReason = reason, FailureDetail = detail, Message = null };
    }
}
