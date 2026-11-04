namespace Notification.Features.Notification.SendBookingConfirmedEmail;

/// <summary>
/// Tells the booking contact that a paid booking is confirmed, with the trip, the seats and
/// passengers, the amount paid and a link to the ticket page. The operator's own email for the same
/// event is a separate slice, so one failing never stops the other.
/// </summary>
public sealed partial class SendBookingConfirmedEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "BookingConfirmed";

    private readonly EmailSendRecorder _recorder;
    private readonly PortalLinks _links;

    public SendBookingConfirmedEmailHandler(EmailSendRecorder recorder, PortalLinks links)
    {
        _recorder = recorder;
        _links = links;
    }

    /// <summary>
    /// Sends the booking confirmed email for one BookingConfirmed event and says whether the
    /// message is done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        var facts = BookingFacts.Read(receivedEvent.Data);
        EmailDraft draft = BuildDraft(receivedEvent, facts);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private EmailDraft BuildDraft(ReceivedEvent receivedEvent, BookingFacts facts)
    {
        var draft = new EmailDraft
        {
            EmailType = EmailType.BookingConfirmed,
            EventType = EventType.BookingConfirmed,
            RepeatKey = receivedEvent.EventId,
            BookingId = facts.BookingId,
            DepartsAt = facts.DepartsAt,
            ToAddress = facts.ContactEmail,
            IsStoppedByCancelledBooking = true,
        };

        if (facts.ContactEmail is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "contact email is missing");
        }

        string? problem = DescribeUnusableData(facts);
        if (problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, problem);
        }

        Uri ticketPage = _links.TicketPage(facts.BookingId!.Value);
        return draft with { Message = BuildConfirmedEmail(facts, ticketPage) };
    }

    private static string? DescribeUnusableData(BookingFacts facts)
    {
        var check = new FieldCheck();
        check.Require("bookingId", facts.BookingId != null);
        check.Require("contact name", facts.ContactName != null);
        check.Require("trip source", facts.Source != null);
        check.Require("trip destination", facts.Destination != null);
        check.Require("departure time", facts.DepartsAt != null);
        check.Require("bus name", facts.BusName != null);
        check.Require("bus type", facts.BusType != null);
        check.Require("isAc", facts.IsAc != null);
        check.Require("seats", BookingEmailLines.HasUsableSeats(facts.Seats));
        check.Require("total amount", facts.TotalAmount != null);
        return check.Problem;
    }
}
