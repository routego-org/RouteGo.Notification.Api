-- =============================================================================
-- V0005__extend_send_log_entries_for_booking_emails.sql
-- extend_send_log_entries_for_booking_emails
-- Applied by the migration runner (migrations/README.md), never by the application.
-- =============================================================================
-- Implements: data-model/notification/send-log-entry.md v0.3.0
-- Adds: repeat_key (ADR 0007), booking_id, departs_at, the skipped status with skip_reason, and the
--   email and event types of the booking, ticket, refund and reminder emails.
-- Change tracking: unchanged - the table stays excluded (see V0003).

BEGIN;

ALTER TABLE notification.send_log_entries
    ADD COLUMN repeat_key text,
    -- external reference: owned by Booking, so no local FK.
    ADD COLUMN booking_id uuid,
    ADD COLUMN departs_at timestamptz,
    ADD COLUMN skip_reason text;

-- Every entry written so far was keyed by its event ID, which is the default repeat key.
UPDATE notification.send_log_entries SET repeat_key = event_id WHERE repeat_key IS NULL;

ALTER TABLE notification.send_log_entries ALTER COLUMN repeat_key SET NOT NULL;

ALTER TABLE notification.send_log_entries
    DROP CONSTRAINT ck_send_log_entries_event_type,
    DROP CONSTRAINT ck_send_log_entries_email_type,
    DROP CONSTRAINT ck_send_log_entries_status;

ALTER TABLE notification.send_log_entries
    ADD CONSTRAINT ck_send_log_entries_event_type
        CHECK (event_type IN ('userRegistered', 'operatorApproved', 'operatorRejected', 'bookingConfirmed',
                              'bookingSeatsChanged', 'bookingCancelled', 'ticketReady', 'refundCompleted',
                              'bookingFailed', 'bookingExpired')),
    ADD CONSTRAINT ck_send_log_entries_email_type
        CHECK (email_type IN ('customerVerification', 'operatorVerification', 'operatorApproved',
                              'operatorRejected', 'bookingConfirmed', 'newBooking', 'ticketReady',
                              'ticketReissued', 'seatsChanged', 'departureReminder', 'bookingCancelled',
                              'refundCompleted', 'bookingFailed', 'bookingExpired')),
    ADD CONSTRAINT ck_send_log_entries_status
        CHECK (status IN ('sending', 'sent', 'failed', 'skipped')),
    ADD CONSTRAINT ck_send_log_entries_skip_reason
        CHECK (skip_reason IN ('suppressedAddress', 'bookingCancelled', 'departurePassed')),
    -- Rule: "skip_reason is set exactly when status is skipped". Value-first, like the rules in V0003.
    ADD CONSTRAINT ck_send_log_entries_skip_reason_when_skipped
        CHECK (('skipped' = status) = (skip_reason IS NOT NULL));

-- Identity / Rule: "Among entries with no resend_of, at most one exists per (repeat key, email type)".
DROP INDEX notification.uq_send_log_entries_event_id_email_type;
CREATE UNIQUE INDEX uq_send_log_entries_repeat_key_email_type
    ON notification.send_log_entries (repeat_key, email_type)
    WHERE resend_of IS NULL;

COMMENT ON TABLE notification.send_log_entries IS 'data-model/notification/send-log-entry.md';

COMMIT;
