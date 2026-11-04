-- =============================================================================
-- V0006__create_known_bookings_and_suppressed_addresses.sql
-- create_known_bookings_and_suppressed_addresses
-- Applied by the migration runner (migrations/README.md), never by the application.
-- =============================================================================
-- Implements: data-model/notification/known-booking.md v0.1.0
--             data-model/notification/suppressed-address.md v0.1.0
-- Change tracking: EXCLUDED for both tables (references/audit.md, "Excluding a table from the audit").
--   Reason: Machine-written from events and email service reports; no signed-in actor ever writes them.
-- Identifier: known_bookings is keyed naturally by the booking identifier ("Notification knows a
--   booking at most once"); suppressed_addresses has a system-assigned id, projected from
--   suppressed_address_id as in V0003.

BEGIN;

CREATE TABLE notification.known_bookings (
    -- external reference: owned by Booking, so no local FK.
    booking_id               uuid        NOT NULL,
    contact_name             text        NOT NULL,
    contact_email            text        NOT NULL,
    trip_source              text        NOT NULL,
    trip_destination         text        NOT NULL,
    departs_at               timestamptz NOT NULL,
    bus_name                 text,
    seat_labels              text,
    seat_change_count        integer     NOT NULL DEFAULT 0,
    status                   text        NOT NULL,
    reminder_state           text        NOT NULL,
    reminder_due_at          timestamptz,
    confirmed_event_id       text,
    confirmed_correlation_id text,
    confirmed_at             timestamptz,
    cancelled_at             timestamptz,

    CONSTRAINT pk_known_bookings PRIMARY KEY (booking_id),
    CONSTRAINT ck_known_bookings_status
        CHECK (status IN ('confirmed', 'cancelled')),
    CONSTRAINT ck_known_bookings_reminder_state
        CHECK (reminder_state IN ('pending', 'planned', 'notPlanned', 'withdrawn', 'done')),
    CONSTRAINT ck_known_bookings_seat_change_count
        CHECK (seat_change_count BETWEEN 0 AND 3),
    -- Rule: "cancelled_at is set exactly when status is cancelled". Value-first, like V0003's rules.
    CONSTRAINT ck_known_bookings_cancelled_at_when_cancelled
        CHECK (('cancelled' = status) = (cancelled_at IS NOT NULL)),
    -- Rule: "reminder_due_at is set exactly when the state is planned, withdrawn or done".
    CONSTRAINT ck_known_bookings_reminder_due_at_when_decided
        CHECK ((reminder_state IN ('planned', 'withdrawn', 'done')) = (reminder_due_at IS NOT NULL))
);

-- The departure reminder job finds the planned reminders that are due.
CREATE INDEX ix_known_bookings_reminder_due_at
    ON notification.known_bookings (reminder_due_at)
    WHERE reminder_state = 'planned';

SELECT audit.fn_exclude_from_audit('notification.known_bookings', 'Machine-written from Booking events and the reminder job; no signed-in actor ever writes it.');
COMMENT ON TABLE notification.known_bookings IS 'data-model/notification/known-booking.md';

CREATE TABLE notification.suppressed_addresses (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    email_address       text        NOT NULL,
    reason              text        NOT NULL,
    provider_message_id text,
    report_detail       text,
    reported_at         timestamptz NOT NULL,
    suppressed_at       timestamptz NOT NULL,

    CONSTRAINT pk_suppressed_addresses PRIMARY KEY (id),
    CONSTRAINT ck_suppressed_addresses_reason
        CHECK (reason IN ('permanentBounce', 'complaint'))
);

-- Identity: "An email address, compared case-insensitively, is on the list at most once." This is
-- also the expression every email checks its recipient with.
CREATE UNIQUE INDEX uq_suppressed_addresses_lower_email_address
    ON notification.suppressed_addresses (lower(email_address));

SELECT audit.fn_exclude_from_audit('notification.suppressed_addresses', 'Machine-written from email service reports and internal-key removals; no signed-in actor ever writes it.');
COMMENT ON TABLE notification.suppressed_addresses IS 'data-model/notification/suppressed-address.md';

COMMIT;
