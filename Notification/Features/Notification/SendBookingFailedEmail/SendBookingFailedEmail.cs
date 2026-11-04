namespace Notification.Features.Notification.SendBookingFailedEmail;

/// <summary>
/// Tells the booking contact that a booking ended without being confirmed because the operator
/// cancelled the trip, or because the seats were lost after payment. Every other failure reason was
/// shown to the customer on screen, so it is consumed without an email and without a send log entry.
/// The booking is not remembered: it can never be reminded, changed or cancelled.
/// </summary>
public sealed partial class SendBookingFailedEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "BookingFailed";

    private const string TripCancelled = "TripCancelled";
    private const string SeatsLost = "SeatsLost";

    private readonly EmailSendRecorder _recorder;

    public SendBookingFailedEmailHandler(EmailSendRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Sends the booking failed email for one BookingFailed event when its reason is emailed, and
    /// says whether the message is done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        string? reason = EventData.Text(receivedEvent.Data, "reason");
        if (reason != TripCancelled && reason != SeatsLost)
        {
            return EventHandlingOutcome.Consumed;
        }

        var facts = BookingFacts.Read(receivedEvent.Data);
        EmailDraft draft = BuildDraft(receivedEvent, facts, reason);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private static EmailDraft BuildDraft(ReceivedEvent receivedEvent, BookingFacts facts, string reason)
    {
        var draft = new EmailDraft
        {
            EmailType = EmailType.BookingFailed,
            EventType = EventType.BookingFailed,
            RepeatKey = receivedEvent.EventId,
            BookingId = facts.BookingId,
            DepartsAt = facts.DepartsAt,
            ToAddress = facts.ContactEmail,
        };

        if (facts.ContactEmail is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "contact email is missing");
        }

        var check = new FieldCheck();
        check.Require("bookingId", facts.BookingId != null);
        check.Require("trip source", facts.Source != null);
        check.Require("trip destination", facts.Destination != null);
        check.Require("departure time", facts.DepartsAt != null);
        if (check.Problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, check.Problem);
        }

        return draft with { Message = BuildFailedEmail(facts, reason == SeatsLost) };
    }
}
