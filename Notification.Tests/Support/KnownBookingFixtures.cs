using Dapper;
using Npgsql;

namespace Notification.Tests.Support;

/// <summary>A known booking a test puts straight into the database, and the way a test reads one back.</summary>
public sealed record StoredKnownBooking
{
    public Guid BookingId { get; init; } = TestBooking.DefaultId;
    public string ContactName { get; init; } = "Ravi Kumar";
    public string ContactEmail { get; init; } = "ravi@gmail.com";
    public string TripSource { get; init; } = "Mumbai";
    public string TripDestination { get; init; } = "Pune";
    public DateTimeOffset DepartsAt { get; init; } = TestBooking.DefaultDeparture;
    public string? BusName { get; init; } = "BusCo Volvo";
    public string? SeatLabels { get; init; } = "12, 13";
    public int SeatChangeCount { get; init; }
    public string Status { get; init; } = "confirmed";
    public string ReminderState { get; init; } = "pending";
    public DateTimeOffset? ReminderDueAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public string? ConfirmedEventId { get; init; }
    public string? ConfirmedCorrelationId { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }

    /// <summary>
    /// A booking with a departure reminder planned for <paramref name="dueAt"/>, as planning leaves it:
    /// confirmed by event "E20", seats "13, 14" after a seat change.
    /// </summary>
    public static StoredKnownBooking WithReminderDue(Guid bookingId, DateTimeOffset departsAt, DateTimeOffset dueAt)
    {
        return new StoredKnownBooking
        {
            BookingId = bookingId,
            DepartsAt = departsAt,
            SeatLabels = "13, 14",
            SeatChangeCount = 1,
            ReminderState = "planned",
            ReminderDueAt = dueAt,
            ConfirmedEventId = "E20",
            ConfirmedCorrelationId = "corr-E20",
            ConfirmedAt = TestEvents.OccurredAt,
        };
    }
}

/// <summary>One known booking as stored.</summary>
public sealed record KnownBookingRow
{
    public Guid BookingId { get; init; }
    public string ContactName { get; init; } = string.Empty;
    public string ContactEmail { get; init; } = string.Empty;
    public string? BusName { get; init; }
    public string? SeatLabels { get; init; }
    public int SeatChangeCount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string ReminderState { get; init; } = string.Empty;
    public DateTime? ReminderDueAt { get; init; }
    public DateTime? CancelledAt { get; init; }
}

/// <summary>An address a test puts on the do-not-send list; a test changes only what its criterion is about, with <c>with</c>.</summary>
public sealed record StoredSuppressedAddress
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EmailAddress { get; init; } = "ravi@gmial.com";
    public string Reason { get; init; } = "permanentBounce";
    public string? ProviderMessageId { get; init; } = "R-777";
    public string? ReportDetail { get; init; } = "smtp; 550 5.1.1 user unknown";
    public DateTimeOffset ReportedAt { get; init; } = TestEvents.OccurredAt;
    public DateTimeOffset SuppressedAt { get; init; } = TestEvents.OccurredAt;
}

/// <summary>One address on the do-not-send list as stored.</summary>
public sealed record SuppressedAddressRow
{
    public Guid Id { get; init; }
    public string EmailAddress { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string? ProviderMessageId { get; init; }
    public string? ReportDetail { get; init; }
    public DateTime ReportedAt { get; init; }
    public DateTime SuppressedAt { get; init; }
}

/// <summary>Puts known bookings and suppressed addresses into the test database, and reads known bookings back.</summary>
public static class KnownBookingFixtures
{
    private const string InsertKnownBookingSql = """
        INSERT INTO notification.known_bookings
            (booking_id, contact_name, contact_email, trip_source, trip_destination, departs_at, bus_name,
             seat_labels, seat_change_count, status, reminder_state, reminder_due_at, cancelled_at,
             confirmed_event_id, confirmed_correlation_id, confirmed_at)
        VALUES
            (@BookingId, @ContactName, @ContactEmail, @TripSource, @TripDestination, @DepartsAt, @BusName,
             @SeatLabels, @SeatChangeCount, @Status, @ReminderState, @ReminderDueAt, @CancelledAt,
             @ConfirmedEventId, @ConfirmedCorrelationId, @ConfirmedAt);
        """;

    private const string GetKnownBookingSql = """
        SELECT booking_id, contact_name, contact_email, bus_name, seat_labels, seat_change_count, status,
               reminder_state, reminder_due_at, cancelled_at
        FROM notification.known_bookings
        WHERE booking_id = @BookingId;
        """;

    private const string ListSuppressedAddressesSql = """
        SELECT id, email_address, reason, provider_message_id, report_detail, reported_at, suppressed_at
        FROM notification.suppressed_addresses
        ORDER BY suppressed_at DESC, id DESC;
        """;

    private const string InsertSuppressedAddressSql = """
        INSERT INTO notification.suppressed_addresses
            (id, email_address, reason, provider_message_id, report_detail, reported_at, suppressed_at)
        VALUES (@Id, @EmailAddress, @Reason, @ProviderMessageId, @ReportDetail, @ReportedAt, @SuppressedAt);
        """;

    /// <summary>Puts <paramref name="booking"/> into the known bookings.</summary>
    public static async Task InsertAsync(DatabaseFixture database, StoredKnownBooking booking)
    {
        await database.SeedAsync(InsertKnownBookingSql, booking);
    }

    /// <summary>Every address on the do-not-send list, newest first.</summary>
    public static async Task<IReadOnlyList<SuppressedAddressRow>> ListSuppressedAsync(DatabaseFixture database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        IEnumerable<SuppressedAddressRow> rows = await connection.QueryAsync<SuppressedAddressRow>(ListSuppressedAddressesSql);
        return rows.ToList();
    }

    /// <summary>Puts a booking Notification knows is cancelled into the known bookings.</summary>
    public static async Task InsertCancelledAsync(DatabaseFixture database, Guid bookingId)
    {
        var booking = new StoredKnownBooking
        {
            BookingId = bookingId,
            Status = "cancelled",
            ReminderState = "notPlanned",
            CancelledAt = EmailTestHost.Now,
        };
        await InsertAsync(database, booking);
    }

    /// <summary>Puts <paramref name="emailAddress"/> on the do-not-send list.</summary>
    public static async Task SuppressAsync(DatabaseFixture database, string emailAddress)
    {
        await InsertSuppressedAsync(database, new StoredSuppressedAddress { EmailAddress = emailAddress });
    }

    /// <summary>Puts <paramref name="address"/> on the do-not-send list and returns its identifier.</summary>
    public static async Task<Guid> InsertSuppressedAsync(DatabaseFixture database, StoredSuppressedAddress address)
    {
        await database.SeedAsync(InsertSuppressedAddressSql, address);
        return address.Id;
    }

    /// <summary>One known booking as stored, or null when Notification does not know it.</summary>
    public static async Task<KnownBookingRow?> GetAsync(DatabaseFixture database, Guid bookingId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        return await connection.QuerySingleOrDefaultAsync<KnownBookingRow>(GetKnownBookingSql, new { BookingId = bookingId });
    }
}
