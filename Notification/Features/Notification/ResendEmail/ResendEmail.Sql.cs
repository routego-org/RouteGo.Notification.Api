namespace Notification.Features.Notification.ResendEmail;

// The send log statements and rows of the resend.
public sealed partial class ResendEmailHandler
{
    private const string LoadOriginalSql = """
        SELECT id, event_id, email_type, booking_id, departs_at, to_address, subject, body_html, body_text,
               failure_reason, event_occurred_at, correlation_id
        FROM notification.send_log_entries
        WHERE id = @Id;
        """;

    // Copied from the resent entry in the database itself, so the new entry carries exactly what was
    // recorded — recipient, subject, both versions of the message, and the event it came from.
    // resend_of being set keeps it clear of the one-entry-per-event rule of the email workers.
    private const string RecordResendSql = """
        INSERT INTO notification.send_log_entries
            (event_id, repeat_key, event_type, email_type, booking_id, departs_at, to_address, subject,
             body_html, body_text, status, resend_of, event_occurred_at, correlation_id, attempted_at)
        SELECT event_id, repeat_key, event_type, email_type, booking_id, departs_at, to_address, subject,
               body_html, body_text, @Status, id, event_occurred_at, correlation_id, @AttemptedAt
        FROM notification.send_log_entries
        WHERE id = @OriginalId
        RETURNING id;
        """;

    private const string IsBookingCancelledSql = """
        SELECT EXISTS (
            SELECT 1 FROM notification.known_bookings
            WHERE booking_id = @BookingId AND status = 'cancelled');
        """;

    private const string FindSuppressionReasonSql = """
        SELECT reason
        FROM notification.suppressed_addresses
        WHERE lower(email_address) = lower(@ToAddress);
        """;

    private const string MarkSentSql = """
        UPDATE notification.send_log_entries
        SET status = @Status, provider_message_id = @ProviderMessageId, sent_at = @SentAt
        WHERE id = @Id
        RETURNING id, event_id, event_type, email_type, booking_id, departs_at, to_address, subject, status,
                  failure_reason, skip_reason, failure_detail, provider_message_id, resend_of, event_occurred_at, correlation_id,
                  attempted_at, sent_at;
        """;

    private const string MarkFailedSql = """
        UPDATE notification.send_log_entries
        SET status = @Status, failure_reason = @FailureReason, failure_detail = @FailureDetail
        WHERE id = @Id;
        """;

    // What the resend needs to know of the entry it resends.
    private sealed record OriginalEntryRow
    {
        public Guid Id { get; init; }
        public string EventId { get; init; } = string.Empty;
        public EmailType EmailType { get; init; }
        public Guid? BookingId { get; init; }
        public DateTime? DepartsAt { get; init; }
        public string? ToAddress { get; init; }
        public string? Subject { get; init; }
        public string? BodyHtml { get; init; }
        public string? BodyText { get; init; }
        public FailureReason? FailureReason { get; init; }
        public DateTime EventOccurredAt { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
    }

    // The new entry as returned to the caller: everything recorded except the message itself.
    private sealed record EntryRow
    {
        public Guid Id { get; init; }
        public string EventId { get; init; } = string.Empty;
        public EventType EventType { get; init; }
        public EmailType EmailType { get; init; }
        public Guid? BookingId { get; init; }
        public DateTime? DepartsAt { get; init; }
        public string? ToAddress { get; init; }
        public string? Subject { get; init; }
        public SendStatus Status { get; init; }
        public FailureReason? FailureReason { get; init; }
        public SkipReason? SkipReason { get; init; }
        public string? FailureDetail { get; init; }
        public string? ProviderMessageId { get; init; }
        public Guid? ResendOf { get; init; }
        public DateTime EventOccurredAt { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
        public DateTime AttemptedAt { get; init; }
        public DateTime? SentAt { get; init; }
    }
}
