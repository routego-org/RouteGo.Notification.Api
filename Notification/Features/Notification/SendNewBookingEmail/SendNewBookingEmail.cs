namespace Notification.Features.Notification.SendNewBookingEmail;

/// <summary>
/// Tells the bus operator about a new booking on its trip: who sits where, and never the booking
/// contact or any amount, exactly what the operator's own passenger list shows. The customer's
/// email for the same event is a separate slice, so one failing never stops the other.
/// </summary>
public sealed partial class SendNewBookingEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "BookingConfirmed";

    private readonly EmailSendRecorder _recorder;
    private readonly PortalLinks _links;

    public SendNewBookingEmailHandler(EmailSendRecorder recorder, PortalLinks links)
    {
        _recorder = recorder;
        _links = links;
    }

    /// <summary>
    /// Sends the new booking email for one BookingConfirmed event and says whether the message is
    /// done with or must be delivered again.
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
            EmailType = EmailType.NewBooking,
            EventType = EventType.BookingConfirmed,
            RepeatKey = receivedEvent.EventId,
            BookingId = facts.BookingId,
            DepartsAt = facts.DepartsAt,
            ToAddress = facts.OperatorEmail,
            IsStoppedByCancelledBooking = true,
        };

        if (facts.OperatorEmail is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "operator email is missing");
        }

        string? problem = DescribeUnusableData(facts);
        if (problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, problem);
        }

        return draft with { Message = BuildNewBookingEmail(facts, _links.OperatorTrips) };
    }

    private static string? DescribeUnusableData(BookingFacts facts)
    {
        var check = new FieldCheck();
        check.Require("bookingId", facts.BookingId != null);
        check.Require("operator name", facts.OperatorName != null);
        check.Require("trip source", facts.Source != null);
        check.Require("trip destination", facts.Destination != null);
        check.Require("departure time", facts.DepartsAt != null);
        check.Require("bus name", facts.BusName != null);
        check.Require("seats", HasUsablePassengers(facts.Seats));
        return check.Problem;
    }

    private static bool HasUsablePassengers(IReadOnlyList<SeatFacts> seats)
    {
        bool hasSeats = BookingEmailLines.HasUsableSeats(seats);
        return hasSeats && seats.All(seat => seat.Age != null && seat.Gender != null);
    }
}
