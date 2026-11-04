namespace Notification.Common.KnownBookings;

/// <summary>
/// Notification's memory of the bookings Booking's events have told it about: the booking contact,
/// the trip, the current seats and whether the booking is cancelled. It is kept so emails about a
/// cancelled booking are not sent, and so the refund email knows whom to write to.
/// </summary>
public sealed partial class KnownBookingStore
{
    private const string WriteAction = "RecordKnownBooking";

    // A booking becomes known as confirmed by its first seat change; its seats change only to a
    // higher seat change count, and a booking known to be cancelled is never changed back.
    private const string RecordSeatChangeSql = """
        INSERT INTO notification.known_bookings
            (booking_id, contact_name, contact_email, trip_source, trip_destination, departs_at,
             bus_name, seat_labels, seat_change_count, status, reminder_state)
        VALUES
            (@BookingId, @ContactName, @ContactEmail, @Source, @Destination, @DepartsAt,
             @BusName, @SeatLabels, @SeatChangeCount, 'confirmed', 'pending')
        ON CONFLICT (booking_id) DO UPDATE
        SET contact_name = EXCLUDED.contact_name, contact_email = EXCLUDED.contact_email,
            trip_source = EXCLUDED.trip_source, trip_destination = EXCLUDED.trip_destination,
            departs_at = EXCLUDED.departs_at, bus_name = EXCLUDED.bus_name,
            seat_labels = EXCLUDED.seat_labels, seat_change_count = EXCLUDED.seat_change_count
        WHERE notification.known_bookings.status = 'confirmed'
          AND notification.known_bookings.seat_change_count < EXCLUDED.seat_change_count;
        """;

    // A reminder not yet due is withdrawn; one not yet decided is never planned. A second
    // cancellation finds the booking cancelled already and changes nothing.
    private const string CancelKnownBookingSql = """
        UPDATE notification.known_bookings
        SET status = 'cancelled', cancelled_at = @CancelledAt,
            reminder_state = CASE reminder_state
                WHEN 'planned' THEN 'withdrawn'
                WHEN 'pending' THEN 'notPlanned'
                ELSE reminder_state END
        WHERE booking_id = @BookingId AND status <> 'cancelled';
        """;

    private const string RecordCancelledBookingSql = """
        INSERT INTO notification.known_bookings
            (booking_id, contact_name, contact_email, trip_source, trip_destination, departs_at,
             seat_change_count, status, reminder_state, cancelled_at)
        VALUES
            (@BookingId, @ContactName, @ContactEmail, @Source, @Destination, @DepartsAt,
             0, 'cancelled', 'notPlanned', @CancelledAt)
        ON CONFLICT (booking_id) DO NOTHING;
        """;

    private const string GetContactSql = """
        SELECT contact_name AS Name, contact_email AS Email
        FROM notification.known_bookings
        WHERE booking_id = @BookingId;
        """;

    private readonly IWriterConnectionFactory _writerFactory;

    public KnownBookingStore(IWriterConnectionFactory writerFactory)
    {
        _writerFactory = writerFactory;
    }

    /// <summary>
    /// Takes the seats of a seat change into the known booking, becoming known as confirmed when
    /// this is the first event heard for the booking. Needs the booking, contact, trip and seats.
    /// </summary>
    public async Task RecordSeatChangeAsync(BookingFacts facts, int seatChangeCount, CancellationToken cancellationToken)
    {
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
            SeatChangeCount = seatChangeCount,
        };
        await ExecuteAsync(RecordSeatChangeSql, parameters, cancellationToken);
    }

    /// <summary>
    /// Records the booking as cancelled for good and withdraws a planned reminder. A booking not
    /// known yet becomes known as cancelled when the event carries its contact and trip; otherwise
    /// there is nothing to remember it by.
    /// </summary>
    public async Task RecordCancellationAsync(BookingFacts facts, DateTimeOffset cancelledAt, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        var parameters = new
        {
            facts.BookingId,
            facts.ContactName,
            facts.ContactEmail,
            facts.Source,
            facts.Destination,
            facts.DepartsAt,
            CancelledAt = cancelledAt,
        };
        int changed = await writer.Connection.ExecuteAsync(new CommandDefinition(
            CancelKnownBookingSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        bool hasWhatToRemember = HasContactAndTrip(facts);
        if (changed == 0 && hasWhatToRemember)
        {
            await writer.Connection.ExecuteAsync(new CommandDefinition(
                RecordCancelledBookingSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        }

        await writer.CommitAsync(cancellationToken);
    }

    /// <summary>The contact Notification remembers for a booking; null when the booking is not known.</summary>
    public async Task<KnownContact?> GetContactAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        KnownContact? contact = await writer.Connection.QuerySingleOrDefaultAsync<KnownContact>(new CommandDefinition(
            GetContactSql, new { BookingId = bookingId }, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
        return contact;
    }

    private static bool HasContactAndTrip(BookingFacts facts)
    {
        bool hasContact = facts.ContactName != null && facts.ContactEmail != null;
        bool hasTrip = facts.Source != null && facts.Destination != null && facts.DepartsAt != null;
        return hasContact && hasTrip && facts.BookingId != null;
    }

    private async Task ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            sql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
    }
}

/// <summary>The booking contact Notification remembers.</summary>
public sealed record KnownContact
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
