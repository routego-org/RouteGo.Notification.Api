-- =============================================================================
-- V0004__index_send_log_entries_by_recipient.sql
-- index_send_log_entries_by_recipient
-- Applied by the migration runner (migrations/README.md), never by the application.
-- =============================================================================
-- Implements: data-model/notification/send-log-entry.md v0.1.0
-- Supports: the send log search (capabilities/notification/features/email-send-log.md), which
--   finds every entry to one address compared case-insensitively, newest attempt first, a page
--   at a time. Without this index every search reads the whole table.

BEGIN;

-- lower(to_address) is the exact expression the search compares on, so the planner can use the
-- index; attempted_at DESC, id DESC is the search's order, so a page is read straight off the
-- index without sorting. Not a partial index on "to_address IS NOT NULL": the planner cannot prove
-- that predicate from the search's lower(to_address) = ... and would then ignore the index.
CREATE INDEX ix_send_log_entries_lower_to_address_attempted_at
    ON notification.send_log_entries (lower(to_address), attempted_at DESC, id DESC);

COMMIT;
