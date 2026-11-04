namespace Notification.Features.Notification.SendCancellationEmail;

/// <summary>
/// Tells the booking contact the booking is cancelled, worded by who cancelled it, with the reason
/// when support or the operator gave one and the refund due, and records the booking as cancelled in
/// Notification's known bookings. The booking is recorded whatever happens to the email. The email
/// is sent even though the booking is cancelled: a cancellation is never news that is stale.
/// </summary>
public sealed partial class SendCancellationEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "BookingCancelled";

    private const string CustomerCancelled = "CustomerCancelled";
    private const string AdminCancelled = "AdminCancelled";
    private const string OperatorCancelled = "OperatorCancelled";
    private const int MaxRefundPercent = 100;

    private readonly EmailSendRecorder _recorder;
    private readonly KnownBookingStore _knownBookings;

    public SendCancellationEmailHandler(EmailSendRecorder recorder, KnownBookingStore knownBookings)
    {
        _recorder = recorder;
        _knownBookings = knownBookings;
    }

    /// <summary>
    /// Records the cancellation and sends the cancellation email for one BookingCancelled event, and
    /// says whether the message is done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        var facts = BookingFacts.Read(receivedEvent.Data);
        Cancellation cancellation = ReadCancellation(receivedEvent.Data);

        if (facts.BookingId != null)
        {
            await _knownBookings.RecordCancellationAsync(facts, receivedEvent.OccurredAt, cancellationToken);
        }

        EmailDraft draft = BuildDraft(receivedEvent, facts, cancellation);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private static EmailDraft BuildDraft(ReceivedEvent receivedEvent, BookingFacts facts, Cancellation cancellation)
    {
        var draft = new EmailDraft
        {
            EmailType = EmailType.BookingCancelled,
            EventType = EventType.BookingCancelled,
            RepeatKey = receivedEvent.EventId,
            BookingId = facts.BookingId,
            ToAddress = facts.ContactEmail,
        };

        if (facts.ContactEmail is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "contact email is missing");
        }

        string? problem = DescribeUnusableData(facts, cancellation);
        if (problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, problem);
        }

        return draft with { Message = BuildCancellationEmail(facts, cancellation) };
    }

    private static string? DescribeUnusableData(BookingFacts facts, Cancellation cancellation)
    {
        bool isKnownKind = cancellation.Kind == CustomerCancelled
            || cancellation.Kind == AdminCancelled
            || cancellation.Kind == OperatorCancelled;
        bool isPercentInRange = cancellation.RefundPercent >= 0 && cancellation.RefundPercent <= MaxRefundPercent;
        var check = new FieldCheck();
        check.Require("bookingId", facts.BookingId != null);
        check.Require("contact name", facts.ContactName != null);
        check.Require("trip source", facts.Source != null);
        check.Require("trip destination", facts.Destination != null);
        check.Require("departure time", facts.DepartsAt != null);
        check.Require("kind (CustomerCancelled, AdminCancelled or OperatorCancelled)", isKnownKind);
        check.Require("refundPercent (0 to 100)", isPercentInRange);
        check.Require("refundAmount", cancellation.RefundAmount != null);
        return check.Problem;
    }

    private static Cancellation ReadCancellation(JsonElement data)
    {
        return new Cancellation
        {
            Kind = EventData.Text(data, "kind"),
            Reason = EventData.Text(data, "reason"),
            RefundPercent = EventData.Whole(data, "refundPercent"),
            RefundAmount = EventData.Whole(data, "refundAmount"),
        };
    }

    // What the cancellation event says beyond the booking itself.
    private sealed record Cancellation
    {
        public string? Kind { get; init; }
        public string? Reason { get; init; }
        public long? RefundPercent { get; init; }
        public long? RefundAmount { get; init; }
    }
}
