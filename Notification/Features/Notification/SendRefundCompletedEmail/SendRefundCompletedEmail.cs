namespace Notification.Features.Notification.SendRefundCompletedEmail;

/// <summary>
/// Tells the customer that Payment has returned the money of a refund. The email goes to the
/// customer email in the event, so a booking Notification never knew is emailed too; the known
/// booking supplies the name and, when the event has no email, the recipient. One refund email is
/// sent per booking, whatever event ID Payment publishes it under.
/// </summary>
public sealed partial class SendRefundCompletedEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "RefundCompleted";

    private readonly EmailSendRecorder _recorder;
    private readonly KnownBookingStore _knownBookings;

    public SendRefundCompletedEmailHandler(EmailSendRecorder recorder, KnownBookingStore knownBookings)
    {
        _recorder = recorder;
        _knownBookings = knownBookings;
    }

    /// <summary>
    /// Sends the refund completed email for one RefundCompleted event and says whether the message
    /// is done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        Guid? bookingId = EventData.Id(receivedEvent.Data, "bookingId");
        long? amount = EventData.Whole(receivedEvent.Data, "amount");
        string? customerEmail = EventData.Text(receivedEvent.Data, "customerEmail");
        KnownContact? known = await LoadKnownContactAsync(bookingId, cancellationToken);

        EmailDraft draft = BuildDraft(receivedEvent, bookingId, amount, ChooseRecipient(customerEmail, known), known);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private async Task<KnownContact?> LoadKnownContactAsync(Guid? bookingId, CancellationToken cancellationToken)
    {
        if (bookingId is null)
        {
            return null;
        }

        return await _knownBookings.GetContactAsync(bookingId.Value, cancellationToken);
    }

    // The event's own customer email wins; the known booking's contact is only a fallback.
    private static string? ChooseRecipient(string? customerEmail, KnownContact? known)
    {
        if (customerEmail != null)
        {
            return customerEmail;
        }

        if (known is null)
        {
            return null;
        }

        return known.Email;
    }

    private static EmailDraft BuildDraft(
        ReceivedEvent receivedEvent, Guid? bookingId, long? amount, string? recipient, KnownContact? known)
    {
        var draft = new EmailDraft
        {
            EmailType = EmailType.RefundCompleted,
            EventType = EventType.RefundCompleted,
            RepeatKey = ChooseRepeatKey(receivedEvent, bookingId),
            BookingId = bookingId,
            ToAddress = recipient,
        };

        if (recipient is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "customer email is missing and the booking is not known");
        }

        var check = new FieldCheck();
        check.Require("bookingId", bookingId != null);
        check.Require("amount", amount != null);
        if (check.Problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, check.Problem);
        }

        return draft with { Message = BuildRefundEmail(bookingId!.Value, amount!.Value, known) };
    }

    // The booking names the refund; an event too incomplete to name one falls back to its own ID.
    private static string ChooseRepeatKey(ReceivedEvent receivedEvent, Guid? bookingId)
    {
        if (bookingId is null)
        {
            return receivedEvent.EventId;
        }

        return bookingId.Value.ToString("D");
    }
}
