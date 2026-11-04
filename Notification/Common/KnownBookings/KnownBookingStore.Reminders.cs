namespace Notification.Common.KnownBookings;

// The departure reminder side of the known bookings: planning a reminder when a booking is
// confirmed, finding the reminders that are due, and marking one done.
public sealed partial class KnownBookingStore
{
    private const string LockForPlanningSql = """
        SELECT booking_id
        FROM notification.known_bookings
        WHERE booking_id = @BookingId
        FOR UPDATE;
        """;

    private const string RecordConfirmedBookingSql = """
        INSERT INTO notification.known_bookings
            (booking_id, contact_name, contact_email, trip_source, trip_destination, departs_at, bus_name,
             seat_labels, seat_change_count, status, reminder_state, reminder_due_at,
             confirmed_event_id, confirmed_correlation_id, confirmed_at)
        VALUES
            (@BookingId, @ContactName, @ContactEmail, @Source, @Destination, @DepartsAt, @BusName,
             @SeatLabels, 0, 'confirmed', @ReminderState, @ReminderDueAt,
             @EventId, @CorrelationId, @ConfirmedAt);
        """;

    // A booking known from a seat change or a cancellation keeps its status, its reminder state if
    // it is decided already, and the seats of a seat change; the confirmation only fills in what
    // it lacks.
    private const string FillInConfirmedDetailsSql = """
        UPDATE notification.known_bookings
        SET bus_name = COALESCE(bus_name, @BusName),
            seat_labels = COALESCE(seat_labels, @SeatLabels),
            confirmed_event_id = COALESCE(confirmed_event_id, @EventId),
            confirmed_correlation_id = COALESCE(confirmed_correlation_id, @CorrelationId),
            confirmed_at = COALESCE(confirmed_at, @ConfirmedAt)
        WHERE booking_id = @BookingId;
        """;

    private const string DecideReminderSql = """
        UPDATE notification.known_bookings
        SET reminder_state = @ReminderState, reminder_due_at = @ReminderDueAt
        WHERE booking_id = @BookingId AND status = 'confirmed' AND reminder_state = 'pending';
        """;

    private const string GetReminderStateSql = """
        SELECT reminder_state
        FROM notification.known_bookings
        WHERE booking_id = @BookingId;
        """;

    private const string ListDueRemindersSql = """
        SELECT booking_id
        FROM notification.known_bookings
        WHERE reminder_state = 'planned' AND reminder_due_at <= @Now
        ORDER BY reminder_due_at
        LIMIT @Limit;
        """;

    private const string GetForReminderSql = """
        SELECT contact_name AS ContactName, contact_email AS ContactEmail, trip_source AS Source,
               trip_destination AS Destination, departs_at AS DepartsAt, bus_name AS BusName,
               seat_labels AS SeatLabels, confirmed_event_id AS ConfirmedEventId,
               confirmed_correlation_id AS ConfirmedCorrelationId, confirmed_at AS ConfirmedAt
        FROM notification.known_bookings
        WHERE booking_id = @BookingId;
        """;

    private const string MarkReminderDoneSql = """
        UPDATE notification.known_bookings
        SET reminder_state = 'done'
        WHERE booking_id = @BookingId AND reminder_state = 'planned';
        """;

    /// <summary>
    /// Takes a confirmed booking into the known bookings and decides its reminder: planned for the
    /// departure time minus <paramref name="leadTime"/> when that is still ahead, otherwise not
    /// planned. A booking already known to be cancelled, or whose reminder is already decided, only
    /// has its missing details filled in. Needs the booking, contact, trip, bus and seats.
    /// </summary>
    public async Task<ReminderPlan> PlanReminderAsync(
        BookingFacts facts, ReceivedEvent confirmed, TimeSpan leadTime, DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset dueAt = facts.DepartsAt!.Value - leadTime;
        ReminderPlan plan = ReminderPlan.NotPlanned;
        if (dueAt > now)
        {
            plan = ReminderPlan.Planned;
        }

        DateTimeOffset? reminderDueAt = null;
        if (plan == ReminderPlan.Planned)
        {
            reminderDueAt = dueAt;
        }

        IEnumerable<string?> labels = facts.Seats.Select(seat => seat.Label);
        var parameters = new
        {
            facts.BookingId,
            facts.ContactName,
            facts.ContactEmail,
            facts.Source,
            facts.Destination,
            facts.DepartsAt,
            facts.BusName,
            SeatLabels = string.Join(", ", labels),
            ReminderState = WireName.Of(plan),
            ReminderDueAt = reminderDueAt,
            confirmed.EventId,
            confirmed.CorrelationId,
            ConfirmedAt = confirmed.OccurredAt,
        };
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        Guid? existing = await writer.Connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            LockForPlanningSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        if (existing is null)
        {
            await writer.Connection.ExecuteAsync(new CommandDefinition(
                RecordConfirmedBookingSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
            await writer.CommitAsync(cancellationToken);
            return plan;
        }

        await writer.Connection.ExecuteAsync(new CommandDefinition(
            FillInConfirmedDetailsSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            DecideReminderSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        string state = await writer.Connection.QuerySingleAsync<string>(new CommandDefinition(
            GetReminderStateSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
        if (state == WireName.Of(ReminderPlan.Planned))
        {
            return ReminderPlan.Planned;
        }

        return ReminderPlan.NotPlanned;
    }

    /// <summary>The bookings whose planned reminder is due at <paramref name="now"/>, earliest first.</summary>
    public async Task<IReadOnlyList<Guid>> ListDueRemindersAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        IEnumerable<Guid> due = await writer.Connection.QueryAsync<Guid>(new CommandDefinition(
            ListDueRemindersSql, new { Now = now, Limit = limit }, writer.Transaction, cancellationToken: cancellationToken));
        var bookings = due.ToList();
        await writer.CommitAsync(cancellationToken);
        return bookings;
    }

    /// <summary>What a departure reminder is built from; null when the booking is not known.</summary>
    public async Task<ReminderBooking?> GetForReminderAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        ReminderBooking? booking = await writer.Connection.QuerySingleOrDefaultAsync<ReminderBooking>(new CommandDefinition(
            GetForReminderSql, new { BookingId = bookingId }, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
        return booking;
    }

    /// <summary>Marks the booking's planned reminder done: the time came and the reminder was handled.</summary>
    public async Task MarkReminderDoneAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        await ExecuteAsync(MarkReminderDoneSql, new { BookingId = bookingId }, cancellationToken);
    }
}

/// <summary>What planning a booking's departure reminder decided.</summary>
public enum ReminderPlan
{
    /// <summary>A reminder is planned for the departure time minus the lead time.</summary>
    Planned,

    /// <summary>No reminder is planned: its time had passed, or the booking was cancelled or its reminder is already over.</summary>
    NotPlanned,
}

/// <summary>What a departure reminder is built from, as the known booking holds it.</summary>
public sealed record ReminderBooking
{
    public string ContactName { get; init; } = string.Empty;
    public string ContactEmail { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public DateTime DepartsAt { get; init; }
    public string? BusName { get; init; }
    public string? SeatLabels { get; init; }
    public string? ConfirmedEventId { get; init; }
    public string? ConfirmedCorrelationId { get; init; }
    public DateTime? ConfirmedAt { get; init; }
}
