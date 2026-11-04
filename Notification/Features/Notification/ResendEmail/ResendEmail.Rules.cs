namespace Notification.Features.Notification.ResendEmail;

// The rules of what may be resent. They are checked in the order the feature spec gives, and the
// first that applies is the one reported.
public sealed partial class ResendEmailHandler
{
    // Booking emails that say nothing useful once the booking is cancelled.
    private static readonly EmailType[] _bookingNewsEmails = new[]
    {
        EmailType.BookingConfirmed,
        EmailType.NewBooking,
        EmailType.TicketReady,
        EmailType.TicketReissued,
        EmailType.SeatsChanged,
        EmailType.DepartureReminder,
    };

    // The emails about a trip that is still ahead: the booking news emails, and the two emails for a
    // booking that did not complete.
    private static readonly EmailType[] _tripEmails = new[]
    {
        EmailType.BookingConfirmed,
        EmailType.NewBooking,
        EmailType.TicketReady,
        EmailType.TicketReissued,
        EmailType.SeatsChanged,
        EmailType.DepartureReminder,
        EmailType.BookingFailed,
        EmailType.BookingExpired,
    };

    private async Task<ResendEmailResult?> RefuseAsync(
        IWriterConnection writer, OriginalEntryRow original, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ResendEmailResult? refusal = RefuseWithNothingToSend(original);
        if (refusal is null)
        {
            refusal = RefuseExpiredLink(original, now);
        }

        if (refusal is null)
        {
            refusal = await RefuseCancelledBookingAsync(writer, original, cancellationToken);
        }

        if (refusal is null)
        {
            refusal = RefuseDepartedTrip(original, now);
        }

        if (refusal is null)
        {
            refusal = await RefuseSuppressedAddressAsync(writer, original, cancellationToken);
        }

        return refusal;
    }

    // An entry recorded without a recipient or without its email has nothing to send.
    private static ResendEmailResult? RefuseWithNothingToSend(OriginalEntryRow original)
    {
        if (original.ToAddress is null || original.Subject is null)
        {
            return new ResendEmailResult { Outcome = ResendEmailOutcome.NothingToSend, FailureReason = original.FailureReason };
        }

        return null;
    }

    // A verification email is useless once its link has expired; every other email has no age limit.
    private ResendEmailResult? RefuseExpiredLink(OriginalEntryRow original, DateTimeOffset now)
    {
        bool carriesVerificationLink = original.EmailType == EmailType.CustomerVerification
            || original.EmailType == EmailType.OperatorVerification;
        if (!carriesVerificationLink)
        {
            return null;
        }

        DateTimeOffset linkExpiredAt = AsUtc(original.EventOccurredAt) + _linkLifetime;
        if (linkExpiredAt > now)
        {
            return null;
        }

        return new ResendEmailResult { Outcome = ResendEmailOutcome.LinkExpired, LinkExpiredAt = linkExpiredAt };
    }

    // The cancellation and refund emails are exactly the ones that may still be resent.
    private static async Task<ResendEmailResult?> RefuseCancelledBookingAsync(
        IWriterConnection writer, OriginalEntryRow original, CancellationToken cancellationToken)
    {
        bool isBookingNews = _bookingNewsEmails.Contains(original.EmailType);
        if (!isBookingNews || original.BookingId is null)
        {
            return null;
        }

        bool isCancelled = await writer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            IsBookingCancelledSql, new { original.BookingId }, writer.Transaction, cancellationToken: cancellationToken));
        if (!isCancelled)
        {
            return null;
        }

        return new ResendEmailResult { Outcome = ResendEmailOutcome.BookingCancelled, BookingId = original.BookingId };
    }

    private static ResendEmailResult? RefuseDepartedTrip(OriginalEntryRow original, DateTimeOffset now)
    {
        bool isTripEmail = _tripEmails.Contains(original.EmailType);
        if (!isTripEmail || original.DepartsAt is null)
        {
            return null;
        }

        DateTimeOffset departsAt = AsUtc(original.DepartsAt.Value);
        if (departsAt > now)
        {
            return null;
        }

        return new ResendEmailResult { Outcome = ResendEmailOutcome.TripDeparted, DepartsAt = departsAt };
    }

    private static async Task<ResendEmailResult?> RefuseSuppressedAddressAsync(
        IWriterConnection writer, OriginalEntryRow original, CancellationToken cancellationToken)
    {
        string? reasonName = await writer.Connection.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(
                FindSuppressionReasonSql, new { original.ToAddress }, writer.Transaction, cancellationToken: cancellationToken));
        if (reasonName is null)
        {
            return null;
        }

        SuppressionReason reason = Enum.Parse<SuppressionReason>(reasonName, ignoreCase: true);
        return new ResendEmailResult
        {
            Outcome = ResendEmailOutcome.AddressSuppressed,
            ToAddress = original.ToAddress,
            SuppressionReason = reason,
        };
    }
}
