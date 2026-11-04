namespace Notification.Features.Notification.SendSeatsChangedEmail;

/// <summary>
/// Tells the booking contact which seats moved and which seats the booking has now, and takes the
/// new seats into Notification's known booking. The known booking is updated whatever happens to
/// the email, and only by a seat change newer than the one it already holds.
/// </summary>
public sealed partial class SendSeatsChangedEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "BookingSeatsChanged";

    private const int MaxSeatChanges = 3;

    private readonly EmailSendRecorder _recorder;
    private readonly KnownBookingStore _knownBookings;
    private readonly PortalLinks _links;

    public SendSeatsChangedEmailHandler(EmailSendRecorder recorder, KnownBookingStore knownBookings, PortalLinks links)
    {
        _recorder = recorder;
        _knownBookings = knownBookings;
        _links = links;
    }

    /// <summary>
    /// Records the seat change and sends the seats changed email for one BookingSeatsChanged event,
    /// and says whether the message is done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        var facts = BookingFacts.Read(receivedEvent.Data);
        long? seatChangeCount = EventData.Whole(receivedEvent.Data, "seatChangeCount");
        IReadOnlyList<SeatMove> moves = ReadMoves(receivedEvent.Data);

        await RememberSeatsAsync(facts, seatChangeCount, cancellationToken);
        EmailDraft draft = BuildDraft(receivedEvent, facts, moves, seatChangeCount);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    // Without a contact, a trip or seats there is nothing the known booking could be remembered by.
    private async Task RememberSeatsAsync(BookingFacts facts, long? seatChangeCount, CancellationToken cancellationToken)
    {
        string? problem = DescribeUnusableSnapshot(facts, seatChangeCount);
        if (problem != null || facts.ContactEmail is null)
        {
            return;
        }

        await _knownBookings.RecordSeatChangeAsync(facts, (int)seatChangeCount!.Value, cancellationToken);
    }

    private EmailDraft BuildDraft(
        ReceivedEvent receivedEvent, BookingFacts facts, IReadOnlyList<SeatMove> moves, long? seatChangeCount)
    {
        var draft = new EmailDraft
        {
            EmailType = EmailType.SeatsChanged,
            EventType = EventType.BookingSeatsChanged,
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

        string? problem = DescribeUnusableSnapshot(facts, seatChangeCount);
        if (problem is null && moves.Count == 0)
        {
            problem = "changes is missing";
        }

        if (problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, problem);
        }

        Uri ticketPage = _links.TicketPage(facts.BookingId!.Value);
        return draft with { Message = BuildSeatsChangedEmail(facts, moves, ticketPage) };
    }

    private static string? DescribeUnusableSnapshot(BookingFacts facts, long? seatChangeCount)
    {
        bool isCountInRange = seatChangeCount >= 1 && seatChangeCount <= MaxSeatChanges;
        var check = new FieldCheck();
        check.Require("bookingId", facts.BookingId != null);
        check.Require("contact name", facts.ContactName != null);
        check.Require("trip source", facts.Source != null);
        check.Require("trip destination", facts.Destination != null);
        check.Require("departure time", facts.DepartsAt != null);
        check.Require("seats", BookingEmailLines.HasUsableSeats(facts.Seats));
        check.Require("seatChangeCount (1 to 3)", isCountInRange);
        return check.Problem;
    }

    private static List<SeatMove> ReadMoves(JsonElement data)
    {
        var moves = new List<SeatMove>();
        foreach (JsonElement change in EventData.Items(data, "changes"))
        {
            string? from = EventData.Text(change, "fromSeatNo");
            string? to = EventData.Text(change, "toSeatNo");
            if (from != null && to != null)
            {
                moves.Add(new SeatMove(from, to));
            }
        }

        return moves;
    }

    // One seat the booking moved from and the seat it moved to.
    private sealed record SeatMove(string FromSeat, string ToSeat);
}
