namespace Notification.Features.Notification.SendBookingExpiredEmail;

/// <summary>
/// Tells the booking contact that the seats held for an unpaid booking were released because
/// payment did not arrive in time, and that nothing was charged. The booking is not remembered.
/// </summary>
public sealed partial class SendBookingExpiredEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "BookingExpired";

    private readonly EmailSendRecorder _recorder;

    public SendBookingExpiredEmailHandler(EmailSendRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Sends the booking expired email for one BookingExpired event and says whether the message is
    /// done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        var facts = BookingFacts.Read(receivedEvent.Data);
        EmailDraft draft = BuildDraft(receivedEvent, facts);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private static EmailDraft BuildDraft(ReceivedEvent receivedEvent, BookingFacts facts)
    {
        var draft = new EmailDraft
        {
            EmailType = EmailType.BookingExpired,
            EventType = EventType.BookingExpired,
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

        return draft with { Message = BuildExpiredEmail(facts) };
    }
}
