using Dapper;
using Notification.Infrastructure.Pgsql;
using Npgsql;

namespace Notification.Tests.Support;

/// <summary>
/// The test side of the send log: the writer the app's handlers use, pointed at the fixture's
/// database, and a direct read of the entries a test then checks.
/// </summary>
public static class SendLogStore
{
    private const string EntriesForEventSql = """
        SELECT id, event_id, repeat_key, event_type, email_type, booking_id, departs_at, to_address, subject, body_html, body_text, status,
               failure_reason, skip_reason, failure_detail, provider_message_id, resend_of, correlation_id,
               event_occurred_at, attempted_at, sent_at
        FROM notification.send_log_entries
        WHERE event_id = @EventId
        ORDER BY attempted_at, id;
        """;

    private const string EntrySql = """
        SELECT id, event_id, repeat_key, event_type, email_type, booking_id, departs_at, to_address, subject, body_html, body_text, status,
               failure_reason, skip_reason, failure_detail, provider_message_id, resend_of, correlation_id,
               event_occurred_at, attempted_at, sent_at
        FROM notification.send_log_entries
        WHERE id = @Id;
        """;

    private const string InsertSql = """
        INSERT INTO notification.send_log_entries
            (id, event_id, repeat_key, event_type, email_type, booking_id, departs_at, to_address, subject,
             body_html, body_text, status, failure_reason, skip_reason, failure_detail, provider_message_id, resend_of, event_occurred_at,
             correlation_id, attempted_at, sent_at)
        VALUES
            (@Id, @EventId, @RepeatKey, @EventType, @EmailType, @BookingId, @DepartsAt, @ToAddress, @Subject,
             @BodyHtml, @BodyText, @Status, @FailureReason, @SkipReason, @FailureDetail, @ProviderMessageId, @ResendOf, @EventOccurredAt,
             @CorrelationId, @AttemptedAt, @SentAt);
        """;

    /// <summary>The app's own writer, as the deployed host builds it, over the fixture's database.</summary>
    public static IWriterConnectionFactory Writer(DatabaseFixture database)
    {
        return new WriterConnectionFactory(
            database.ConnectionString, SystemActor.Default, ConnectionRetryOptions.ManagedInstances);
    }

    /// <summary>The app's own reader, over the fixture's database.</summary>
    public static IReaderConnectionFactory Reader(DatabaseFixture database)
    {
        return new ReaderConnectionFactory(database.ConnectionString);
    }

    /// <summary>Every entry recorded for one event, oldest attempt first.</summary>
    public static async Task<IReadOnlyList<SendLogEntryRow>> EntriesForEventAsync(DatabaseFixture database, string eventId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        IEnumerable<SendLogEntryRow> rows =
            await connection.QueryAsync<SendLogEntryRow>(EntriesForEventSql, new { EventId = eventId });
        return rows.ToList();
    }

    /// <summary>Puts <paramref name="entry"/> straight into the send log and returns its identifier.</summary>
    public static async Task<Guid> InsertAsync(DatabaseFixture database, StoredSendLogEntry entry)
    {
        await database.SeedAsync(InsertSql, entry);
        return entry.Id;
    }

    /// <summary>One entry as stored, or null when no entry has that identifier.</summary>
    public static async Task<SendLogEntryRow?> EntryAsync(DatabaseFixture database, Guid id)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        return await connection.QuerySingleOrDefaultAsync<SendLogEntryRow>(EntrySql, new { Id = id });
    }
}
