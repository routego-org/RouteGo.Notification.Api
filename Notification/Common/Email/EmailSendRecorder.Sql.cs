namespace Notification.Common.Email;

// The send log statements shared by every email that is recorded through the recorder.
public sealed partial class EmailSendRecorder
{
    // The unique index on (repeat_key, email_type) for non-resends makes "record unless already
    // recorded" one atomic step: a repeat delivery inserts nothing and goes on to lock the entry.
    private const string RecordNewAttemptSql = """
        INSERT INTO notification.send_log_entries
            (event_id, repeat_key, event_type, email_type, booking_id, departs_at, to_address, subject,
             body_html, body_text, status, failure_reason, skip_reason, failure_detail,
             event_occurred_at, correlation_id, attempted_at)
        VALUES
            (@EventId, @RepeatKey, @EventType, @EmailType, @BookingId, @DepartsAt, @ToAddress, @Subject,
             @BodyHtml, @BodyText, @Status, @FailureReason, @SkipReason, @FailureDetail,
             @EventOccurredAt, @CorrelationId, @AttemptedAt)
        ON CONFLICT (repeat_key, email_type) WHERE resend_of IS NULL DO NOTHING
        RETURNING id;
        """;

    private const string LockExistingSql = """
        SELECT id, status
        FROM notification.send_log_entries
        WHERE repeat_key = @RepeatKey AND email_type = @EmailType AND resend_of IS NULL
        FOR UPDATE;
        """;

    // A repeat delivery evaluates the email afresh, so everything the decision can change is
    // rewritten: the recipient and message may be present now, or a rule may no longer apply.
    private const string RetryExistingSql = """
        UPDATE notification.send_log_entries
        SET booking_id = @BookingId, departs_at = @DepartsAt, to_address = @ToAddress, subject = @Subject,
            body_html = @BodyHtml, body_text = @BodyText, status = @Status, failure_reason = @FailureReason,
            skip_reason = @SkipReason, failure_detail = @FailureDetail
        WHERE id = @Id;
        """;

    private const string MarkSentSql = """
        UPDATE notification.send_log_entries
        SET status = @Status, failure_reason = NULL, skip_reason = NULL,
            provider_message_id = @ProviderMessageId, sent_at = @SentAt
        WHERE id = @Id;
        """;

    private const string MarkFailedSql = """
        UPDATE notification.send_log_entries
        SET status = @Status, failure_reason = @FailureReason, failure_detail = @FailureDetail
        WHERE id = @Id;
        """;

    private const string NoteTemporaryFailureSql = """
        UPDATE notification.send_log_entries
        SET failure_detail = @FailureDetail
        WHERE id = @Id;
        """;

    private const string IsBookingCancelledSql = """
        SELECT EXISTS (
            SELECT 1 FROM notification.known_bookings
            WHERE booking_id = @BookingId AND status = 'cancelled');
        """;

    private const string IsAddressSuppressedSql = """
        SELECT EXISTS (
            SELECT 1 FROM notification.suppressed_addresses
            WHERE lower(email_address) = lower(@ToAddress));
        """;
}
