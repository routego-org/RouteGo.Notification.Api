-- =============================================================================
-- V0003__create_notification_send_log_entries.sql
-- create_notification_send_log_entries
-- Applied by the migration runner (migrations/README.md), never by the application.
-- =============================================================================
-- Implements: data-model/notification/send-log-entry.md v0.1.0
-- Change tracking: EXCLUDED (references/audit.md, "Excluding a table from the audit").
--   Reason: Machine-written record of every email attempt: it is itself the history support reads, and no signed-in actor ever writes it (email workers and internal-key resends only).
-- Identifier: the entity's identity attribute send_log_entry_id is projected as the surrogate
--   primary key `id` (uuid; the contract's sendLogEntryId is format uuid). The name differs from the
--   attribute on purpose and is reported upstream as drift for the spec owner to settle.

BEGIN;

-- One area, one schema: data-model/notification/ -> notification.
CREATE SCHEMA IF NOT EXISTS notification;

CREATE TABLE notification.send_log_entries (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    -- external reference: owned by the producing service, so text and no local FK.
    event_id            text        NOT NULL,
    event_type          text        NOT NULL,
    email_type          text        NOT NULL,
    to_address          text,
    subject             text,
    body_html           text,
    body_text           text,
    status              text        NOT NULL,
    failure_reason      text,
    failure_detail      text,
    provider_message_id text,
    resend_of           uuid,
    event_occurred_at   timestamptz NOT NULL,
    correlation_id      text        NOT NULL,
    attempted_at        timestamptz NOT NULL,
    sent_at             timestamptz,

    -- No audit columns: this table is declared excluded below. fn_exclude_from_audit
    -- refuses a table that carries any of them - audited or excluded, never half.
    CONSTRAINT pk_send_log_entries PRIMARY KEY (id),
    -- Relationship: "May be the resend of one other SendLogEntry".
    CONSTRAINT fk_send_log_entries_resend_of FOREIGN KEY (resend_of)
        REFERENCES notification.send_log_entries (id),
    CONSTRAINT ck_send_log_entries_event_type
        CHECK (event_type IN ('userRegistered', 'operatorApproved', 'operatorRejected')),
    CONSTRAINT ck_send_log_entries_email_type
        CHECK (email_type IN ('customerVerification', 'operatorVerification', 'operatorApproved', 'operatorRejected')),
    CONSTRAINT ck_send_log_entries_status
        CHECK (status IN ('sending', 'sent', 'failed')),
    CONSTRAINT ck_send_log_entries_failure_reason
        CHECK (failure_reason IN ('missingRecipient', 'invalidEventData', 'refusedByEmailService', 'emailServiceUnavailable')),
    -- The two rules below compare value-first ('failed' = status) so the schema drift check, which
    -- reads the first CHECK comparing status to a literal as the column's value list, finds
    -- ck_send_log_entries_status rather than one of these.
    -- Rule: "failure_reason is set exactly when status is failed".
    CONSTRAINT ck_send_log_entries_failure_reason_when_failed
        CHECK (('failed' = status) = (failure_reason IS NOT NULL)),
    -- Rule: "provider_message_id and sent_at are set exactly when status is sent".
    CONSTRAINT ck_send_log_entries_receipt_when_sent
        CHECK (('sent' = status) = (provider_message_id IS NOT NULL)
           AND ('sent' = status) = (sent_at IS NOT NULL)),
    -- Rule: "subject, body_html and body_text are set together, or all empty".
    CONSTRAINT ck_send_log_entries_content_together
        CHECK ((subject IS NULL) = (body_html IS NULL)
           AND (subject IS NULL) = (body_text IS NULL))
);

-- Identity / Rule: "Among entries with no resend_of, at most one exists per (event ID, email type)".
CREATE UNIQUE INDEX uq_send_log_entries_event_id_email_type
    ON notification.send_log_entries (event_id, email_type)
    WHERE resend_of IS NULL;

-- Supports the self-referencing foreign key.
CREATE INDEX ix_send_log_entries_resend_of ON notification.send_log_entries (resend_of);

-- Declare the exclusion, with its reason, so the CI audit gate and the checkers PASS for
-- this table and the database itself says why it has no history. Idempotent.
SELECT audit.fn_exclude_from_audit('notification.send_log_entries', 'Machine-written record of every email attempt: it is itself the history support reads, and no signed-in actor ever writes it (email workers and internal-key resends only).');

-- Traceability: the in-database pointer back to the logical model (checked by CI).
COMMENT ON TABLE notification.send_log_entries IS 'data-model/notification/send-log-entry.md';

COMMIT;
