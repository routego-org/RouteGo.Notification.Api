namespace Notification.Features.Notification.SendDepartureReminderEmail;

/// <summary>
/// Sends a booking's departure reminder when it is due, built from the known booking at that
/// moment so it shows the seats after any seat change. A cancelled booking, a departure already
/// passed and an address on the do-not-send list are skipped. The reminder is done once it has been
/// handled for good; one that failed temporarily stays planned to be tried again.
/// </summary>
public sealed partial class SendDepartureReminderEmailHandler
{
    private readonly EmailSendRecorder _recorder;
    private readonly KnownBookingStore _knownBookings;
    private readonly PortalLinks _links;

    public SendDepartureReminderEmailHandler(EmailSendRecorder recorder, KnownBookingStore knownBookings, PortalLinks links)
    {
        _recorder = recorder;
        _knownBookings = knownBookings;
        _links = links;
    }

    /// <summary>
    /// Sends the reminder of one booking and says whether it is done with or must be tried again.
    /// <paramref name="isLastAttempt"/> says that a temporary failure now parks the reminder.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(Guid bookingId, bool isLastAttempt, CancellationToken cancellationToken)
    {
        ReminderBooking? booking = await _knownBookings.GetForReminderAsync(bookingId, cancellationToken);
        if (booking is null)
        {
            return EventHandlingOutcome.Consumed;
        }

        ReceivedEvent confirmed = ToConfirmedEvent(booking, isLastAttempt);
        EmailDraft draft = BuildDraft(bookingId, booking);
        EventHandlingOutcome outcome = await _recorder.SendAsync(confirmed, draft, cancellationToken);
        if (outcome == EventHandlingOutcome.Consumed || isLastAttempt)
        {
            await _knownBookings.MarkReminderDoneAsync(bookingId, cancellationToken);
        }

        return outcome;
    }

    // The send log entry of a reminder records the BookingConfirmed that planned it as its event. A
    // booking with a planned reminder always has those details: planning records them together.
    private static ReceivedEvent ToConfirmedEvent(ReminderBooking booking, bool isLastAttempt)
    {
        return new ReceivedEvent
        {
            EventId = booking.ConfirmedEventId!,
            EventType = "BookingConfirmed",
            OccurredAt = new DateTimeOffset(booking.ConfirmedAt!.Value, TimeSpan.Zero),
            CorrelationId = booking.ConfirmedCorrelationId!,
            Data = default,
            IsLastAttempt = isLastAttempt,
        };
    }

    private EmailDraft BuildDraft(Guid bookingId, ReminderBooking booking)
    {
        var departsAt = new DateTimeOffset(booking.DepartsAt, TimeSpan.Zero);
        var draft = new EmailDraft
        {
            EmailType = EmailType.DepartureReminder,
            EventType = EventType.BookingConfirmed,
            RepeatKey = bookingId.ToString("D"),
            BookingId = bookingId,
            DepartsAt = departsAt,
            ToAddress = booking.ContactEmail,
            IsStoppedByCancelledBooking = true,
            IsStoppedByDeparture = true,
        };

        var check = new FieldCheck();
        check.Require("bus name", booking.BusName != null);
        check.Require("seats", booking.SeatLabels != null);
        if (check.Problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, check.Problem);
        }

        return draft with { Message = BuildReminderEmail(bookingId, booking, departsAt, _links.TicketPage(bookingId)) };
    }
}
