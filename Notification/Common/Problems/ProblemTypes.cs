namespace Notification.Common.Problems;

/// <summary>
/// Projection of the product's problem-type catalog (contracts/problem-types.md in the
/// product-specs repo). Codes are never minted here — a new code starts life in the catalog
/// and lands here as a constant carrying the same code and the same type URI, so this class
/// can be diffed against the catalog rather than trusted to match it.
/// <para>
/// Seeded with every baseline code this codebase itself emits. The baseline rows that are
/// missing are emitted by middleware this project does not own — 403 by the authorization
/// handler, 429 by the rate limiter — and 400 validation problems are identified by their
/// <c>errors</c> member rather than a code, by contract. Append one constant per product code
/// as capabilities emit them (references/host-components.md).
/// </para>
/// </summary>
public static class ProblemTypes
{
    // A URN rather than a web address, because RouteGo has no fixed public host for problem
    // documentation. Every type URI is this prefix, a colon, and the code in kebab-case.
    private const string ProblemsBase = "urn:routego:notification:problems";

    /// <summary>401 — the internal key is missing or wrong. Emitted by the internal-key authentication handler.</summary>
    public static readonly ProblemType Unauthorized = new("UNAUTHORIZED", $"{ProblemsBase}:unauthorized");

    /// <summary>404 — the resource does not exist.</summary>
    public static readonly ProblemType NotFound = new("NOT_FOUND", $"{ProblemsBase}:not-found");

    /// <summary>409 — state changed since read, or the request duplicates existing state.</summary>
    public static readonly ProblemType Conflict = new("CONFLICT", $"{ProblemsBase}:conflict");

    /// <summary>422 — a business rule refused the request and no specific code exists yet.</summary>
    public static readonly ProblemType BusinessRuleViolated =
        new("BUSINESS_RULE_VIOLATED", $"{ProblemsBase}:business-rule-violated");

    /// <summary>502 — a required dependency is down or returned an unusable response.</summary>
    public static readonly ProblemType UpstreamUnavailable =
        new("UPSTREAM_UNAVAILABLE", $"{ProblemsBase}:upstream-unavailable");

    /// <summary>504 — a required dependency did not respond in time and no data could be served.</summary>
    public static readonly ProblemType UpstreamTimeout =
        new("UPSTREAM_TIMEOUT", $"{ProblemsBase}:upstream-timeout");

    /// <summary>500 — unhandled server fault. Emitted by UnhandledExceptionHandler, never by a slice.</summary>
    public static readonly ProblemType InternalError =
        new("INTERNAL_ERROR", $"{ProblemsBase}:internal-error");

    /// <summary>422 — the entry has no recipient or no email content, so there is nothing to resend.</summary>
    public static readonly ProblemType ResendNotPossible =
        new("RESEND_NOT_POSSIBLE", $"{ProblemsBase}:resend-not-possible");

    /// <summary>422 — the verification link in the email to resend has expired.</summary>
    public static readonly ProblemType ResendLinkExpired =
        new("RESEND_LINK_EXPIRED", $"{ProblemsBase}:resend-link-expired");

    /// <summary>422 — the email service permanently refused the resent email; a new Failed entry was recorded.</summary>
    public static readonly ProblemType EmailRefused =
        new("EMAIL_REFUSED", $"{ProblemsBase}:email-refused");

    /// <summary>422 — the booking of the email to resend is known to be cancelled.</summary>
    public static readonly ProblemType ResendBookingCancelled =
        new("RESEND_BOOKING_CANCELLED", $"{ProblemsBase}:resend-booking-cancelled");

    /// <summary>422 — the trip of the email to resend has already departed.</summary>
    public static readonly ProblemType ResendTripDeparted =
        new("RESEND_TRIP_DEPARTED", $"{ProblemsBase}:resend-trip-departed");

    /// <summary>422 — the recipient of the email to resend is on the do-not-send list.</summary>
    public static readonly ProblemType AddressSuppressed =
        new("ADDRESS_SUPPRESSED", $"{ProblemsBase}:address-suppressed");

    /// <summary>Worker code, no HTTP status — a booking's departure reminder could not be planned:
    /// the event data was unusable, or planning failed through every automatic retry. Reaches the
    /// platform engineer through the error log.</summary>
    public static readonly ProblemType ReminderNotPlanned =
        new("REMINDER_NOT_PLANNED", $"{ProblemsBase}:reminder-not-planned");

    /// <summary>Worker code, no HTTP status — a bounce or complaint report could not be recorded
    /// through every automatic retry and was parked. Reaches the platform engineer through the
    /// error log and the alarm on the parked-messages queue.</summary>
    public static readonly ProblemType EmailReportParked =
        new("EMAIL_REPORT_PARKED", $"{ProblemsBase}:email-report-parked");

    /// <summary>Worker code, no HTTP status — an email failed temporarily through every automatic
    /// retry and was parked. Reaches the platform engineer through the error log and the alarm on
    /// the parked-messages queue, never an HTTP client.</summary>
    public static readonly ProblemType EmailSendParked =
        new("EMAIL_SEND_PARKED", $"{ProblemsBase}:email-send-parked");
}
