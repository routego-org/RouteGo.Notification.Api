namespace Notification.Features.Notification.PlanDepartureReminder;

/// <summary>
/// Remembers a confirmed booking and plans its departure reminder for the departure time minus the
/// reminder lead time, or plans none when that time has already passed. A booking already known to
/// be cancelled, or already planned, is left as it is. Unusable event data records nothing and
/// raises the problem for the platform engineer without a retry; a reminder that cannot be recorded
/// is retried and, after repeated failure, raised the same way.
/// </summary>
public sealed class PlanDepartureReminderHandler
{
    /// <summary>The producer's event type that starts this planning.</summary>
    public const string TriggerEventType = "BookingConfirmed";

    private const string ReasonInvalidEventData = "invalidEventData";
    private const string ReasonRepeatedFailure = "repeatedFailure";

    private readonly KnownBookingStore _knownBookings;
    private readonly IClock _clock;
    private readonly ILogger<PlanDepartureReminderHandler> _logger;

    public PlanDepartureReminderHandler(
        KnownBookingStore knownBookings, IClock clock, ILogger<PlanDepartureReminderHandler> logger)
    {
        _knownBookings = knownBookings;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Plans the reminder for one BookingConfirmed event and says whether the message is done with
    /// or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(
        ReceivedEvent receivedEvent, TimeSpan leadTime, CancellationToken cancellationToken)
    {
        var facts = BookingFacts.Read(receivedEvent.Data);
        string? problem = DescribeUnusableData(facts);
        if (problem != null)
        {
            _logger.LogError(
                "{ProblemCode}: no reminder for booking {BookingId} (event {EventId}, correlation {CorrelationId}, reason {Reason}): {Problem}",
                ProblemTypes.ReminderNotPlanned.Code, facts.BookingId, receivedEvent.EventId,
                receivedEvent.CorrelationId, ReasonInvalidEventData, problem);
            return EventHandlingOutcome.Consumed;
        }

        try
        {
            ReminderPlan plan = await _knownBookings.PlanReminderAsync(
                facts, receivedEvent, leadTime, _clock.UtcNow, cancellationToken);
            _logger.LogInformation(
                "Booking {BookingId} (event {EventId}, correlation {CorrelationId}) is known; reminder {Plan}",
                facts.BookingId, receivedEvent.EventId, receivedEvent.CorrelationId, plan);
            return EventHandlingOutcome.Consumed;
        }
        catch (DbException exception)
        {
            LogPlanningFailure(receivedEvent, facts, exception);
            return EventHandlingOutcome.RetryLater;
        }
    }

    // On the last attempt the alert is raised here; the message itself is left on the queue, which
    // then moves it to the parked-messages queue for a replay after the fix.
    private void LogPlanningFailure(ReceivedEvent receivedEvent, BookingFacts facts, DbException exception)
    {
        if (!receivedEvent.IsLastAttempt)
        {
            _logger.LogWarning(
                exception, "Reminder for booking {BookingId} (event {EventId}, correlation {CorrelationId}) not planned yet, will retry",
                facts.BookingId, receivedEvent.EventId, receivedEvent.CorrelationId);
            return;
        }

        _logger.LogError(
            exception,
            "{ProblemCode}: no reminder for booking {BookingId} (event {EventId}, correlation {CorrelationId}, reason {Reason}) after repeated failure",
            ProblemTypes.ReminderNotPlanned.Code, facts.BookingId, receivedEvent.EventId,
            receivedEvent.CorrelationId, ReasonRepeatedFailure);
    }

    private static string? DescribeUnusableData(BookingFacts facts)
    {
        var check = new FieldCheck();
        check.Require("bookingId", facts.BookingId != null);
        check.Require("contact name", facts.ContactName != null);
        check.Require("contact email", facts.ContactEmail != null);
        check.Require("trip source", facts.Source != null);
        check.Require("trip destination", facts.Destination != null);
        check.Require("departure time", facts.DepartsAt != null);
        check.Require("bus name", facts.BusName != null);
        check.Require("seats", BookingEmailLines.HasUsableSeats(facts.Seats));
        return check.Problem;
    }
}
