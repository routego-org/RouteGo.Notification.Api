namespace Notification.Common.Email;

/// <summary>
/// Sends one decided email and records every step in the send log: the attempt, the rules that
/// skip it, the email service's answer and, after repeated temporary failure, the parked alert. A
/// repeat delivery of an email already sent sends nothing; an earlier attempt that is still
/// sending, failed or skipped is evaluated again on the same entry.
/// </summary>
public sealed partial class EmailSendRecorder
{
    private const string WriteAction = "SendEmail";

    private readonly EmailWorkerPorts _ports;
    private readonly ILogger<EmailSendRecorder> _logger;

    public EmailSendRecorder(EmailWorkerPorts ports, ILogger<EmailSendRecorder> logger)
    {
        _ports = ports;
        _logger = logger;
    }

    /// <summary>
    /// Records and, when no rule stops it, sends the email, and says whether the message is done
    /// with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> SendAsync(
        ReceivedEvent receivedEvent, EmailDraft draft, CancellationToken cancellationToken)
    {
        RecordedAttempt recorded = await RecordAttemptAsync(receivedEvent, draft, cancellationToken);
        if (recorded.IsAlreadySent)
        {
            _logger.LogInformation(
                "{EmailType} email for event {EventId} (correlation {CorrelationId}) was already sent; repeat delivery ignored",
                draft.EmailType, receivedEvent.EventId, receivedEvent.CorrelationId);
            return EventHandlingOutcome.Consumed;
        }

        if (recorded.Decision.Status != SendStatus.Sending)
        {
            _logger.LogInformation(
                "{EmailType} email for event {EventId} (correlation {CorrelationId}) not sent: {Status} {Reason}",
                draft.EmailType, receivedEvent.EventId, receivedEvent.CorrelationId,
                recorded.Decision.Status, recorded.Decision.Describe());
            return EventHandlingOutcome.Consumed;
        }

        EmailSendResult result = await _ports.EmailSender.SendAsync(draft.ToAddress!, draft.Message!, cancellationToken);
        var sending = new SendingEntry(recorded.Id, receivedEvent, draft.EmailType);
        return await RecordResultAsync(sending, result, cancellationToken);
    }

    private async Task<RecordedAttempt> RecordAttemptAsync(
        ReceivedEvent receivedEvent, EmailDraft draft, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _ports.Writer.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        AttemptDecision decision = await DecideAsync(writer, draft, _ports.Clock.UtcNow, cancellationToken);
        object parameters = AttemptParameters(receivedEvent, draft, decision, _ports.Clock.UtcNow, null);

        Guid? newId = await writer.Connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            RecordNewAttemptSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        if (newId != null)
        {
            await writer.CommitAsync(cancellationToken);
            return new RecordedAttempt(newId.Value, false, decision);
        }

        ExistingEntryRow existing = await writer.Connection.QuerySingleAsync<ExistingEntryRow>(new CommandDefinition(
            LockExistingSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        if (SendLogRules.BlocksRepeat(existing.Status))
        {
            await writer.CommitAsync(cancellationToken);
            return new RecordedAttempt(existing.Id, true, decision);
        }

        object retryParameters = AttemptParameters(receivedEvent, draft, decision, _ports.Clock.UtcNow, existing.Id);
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            RetryExistingSql, retryParameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
        return new RecordedAttempt(existing.Id, false, decision);
    }

    // Failures found while building the email come first, then the booking rules, then the
    // do-not-send list: the order the shared rules give.
    private static async Task<AttemptDecision> DecideAsync(
        IWriterConnection writer, EmailDraft draft, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (draft.FailureReason != null)
        {
            return AttemptDecision.Failed(draft.FailureReason.Value, draft.FailureDetail);
        }

        if (draft.IsStoppedByCancelledBooking && draft.BookingId != null)
        {
            bool isCancelled = await writer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                IsBookingCancelledSql, new { BookingId = draft.BookingId }, writer.Transaction,
                cancellationToken: cancellationToken));
            if (isCancelled)
            {
                return AttemptDecision.Skipped(SkipReason.BookingCancelled);
            }
        }

        if (draft.IsStoppedByDeparture && draft.DepartsAt <= now)
        {
            return AttemptDecision.Skipped(SkipReason.DeparturePassed);
        }

        bool isSuppressed = await writer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            IsAddressSuppressedSql, new { draft.ToAddress }, writer.Transaction,
            cancellationToken: cancellationToken));
        if (isSuppressed)
        {
            return AttemptDecision.Skipped(SkipReason.SuppressedAddress);
        }

        return AttemptDecision.Sending();
    }

    private async Task<EventHandlingOutcome> RecordResultAsync(
        SendingEntry sending, EmailSendResult result, CancellationToken cancellationToken)
    {
        Guid entryId = sending.EntryId;
        ReceivedEvent receivedEvent = sending.Event;
        EmailType emailType = sending.EmailType;
        switch (result.Outcome)
        {
            case EmailSendOutcome.Accepted:
                await UpdateEntryAsync(MarkSentSql, SentParameters(entryId, result.ProviderMessageId!, _ports.Clock.UtcNow), cancellationToken);
                _logger.LogInformation(
                    "{EmailType} email for event {EventId} (correlation {CorrelationId}) sent",
                    emailType, receivedEvent.EventId, receivedEvent.CorrelationId);
                return EventHandlingOutcome.Consumed;
            case EmailSendOutcome.Refused:
                await UpdateEntryAsync(MarkFailedSql, FailedParameters(entryId, FailureReason.RefusedByEmailService, result.Detail), cancellationToken);
                _logger.LogInformation(
                    "{EmailType} email for event {EventId} (correlation {CorrelationId}) refused by the email service",
                    emailType, receivedEvent.EventId, receivedEvent.CorrelationId);
                return EventHandlingOutcome.Consumed;
            case EmailSendOutcome.Unavailable:
                await RecordTemporaryFailureAsync(sending, result.Detail, cancellationToken);
                return EventHandlingOutcome.RetryLater;
            default:
                throw new InvalidOperationException($"Unhandled email send outcome '{result.Outcome}'.");
        }
    }

    // On the last attempt the entry fails and the alert is raised here; the message itself is left
    // on the queue, which then moves it to the parked-messages queue for a later replay.
    private async Task RecordTemporaryFailureAsync(
        SendingEntry sending, string? lastError, CancellationToken cancellationToken)
    {
        Guid entryId = sending.EntryId;
        ReceivedEvent receivedEvent = sending.Event;
        EmailType emailType = sending.EmailType;
        if (!receivedEvent.IsLastAttempt)
        {
            await UpdateEntryAsync(NoteTemporaryFailureSql, new { Id = entryId, FailureDetail = lastError }, cancellationToken);
            _logger.LogWarning(
                "{EmailType} email for event {EventId} (correlation {CorrelationId}) not sent yet, will retry: {LastError}",
                emailType, receivedEvent.EventId, receivedEvent.CorrelationId, lastError);
            return;
        }

        await UpdateEntryAsync(
            MarkFailedSql, FailedParameters(entryId, FailureReason.EmailServiceUnavailable, lastError), cancellationToken);
        _logger.LogError(
            "{ProblemCode}: email for event {EventId} ({EventType}, {EmailType}, correlation {CorrelationId}) parked after repeated failure: {LastError}",
            ProblemTypes.EmailSendParked.Code, receivedEvent.EventId, receivedEvent.EventType, emailType,
            receivedEvent.CorrelationId, lastError);
    }

    private async Task UpdateEntryAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _ports.Writer.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            sql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
    }

    private static object AttemptParameters(
        ReceivedEvent receivedEvent, EmailDraft draft, AttemptDecision decision, DateTimeOffset attemptedAt, Guid? entryId)
    {
        string? failureReason = null;
        if (decision.FailureReason != null)
        {
            failureReason = WireName.Of(decision.FailureReason.Value);
        }

        string? skipReason = null;
        if (decision.SkipReason != null)
        {
            skipReason = WireName.Of(decision.SkipReason.Value);
        }

        EmailMessage? message = draft.Message;
        string? subject = null;
        string? bodyHtml = null;
        string? bodyText = null;
        if (message != null)
        {
            subject = message.Subject;
            bodyHtml = message.HtmlBody;
            bodyText = message.TextBody;
        }

        return new
        {
            Id = entryId,
            receivedEvent.EventId,
            draft.RepeatKey,
            EventType = WireName.Of(draft.EventType),
            EmailType = WireName.Of(draft.EmailType),
            draft.BookingId,
            draft.DepartsAt,
            draft.ToAddress,
            Subject = subject,
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            Status = WireName.Of(decision.Status),
            FailureReason = failureReason,
            SkipReason = skipReason,
            FailureDetail = decision.Detail,
            EventOccurredAt = receivedEvent.OccurredAt,
            receivedEvent.CorrelationId,
            AttemptedAt = attemptedAt,
        };
    }

    private static object SentParameters(Guid entryId, string providerMessageId, DateTimeOffset sentAt)
    {
        return new
        {
            Id = entryId,
            Status = WireName.Of(SendStatus.Sent),
            ProviderMessageId = providerMessageId,
            SentAt = sentAt,
        };
    }

    private static object FailedParameters(Guid entryId, FailureReason reason, string? detail)
    {
        return new
        {
            Id = entryId,
            Status = WireName.Of(SendStatus.Failed),
            FailureReason = WireName.Of(reason),
            FailureDetail = detail,
        };
    }

    // What the rules decided for this attempt: sending it, failing it, or skipping it.
    private sealed record AttemptDecision(SendStatus Status, FailureReason? FailureReason, SkipReason? SkipReason, string? Detail)
    {
        public static AttemptDecision Sending()
        {
            return new AttemptDecision(SendStatus.Sending, null, null, null);
        }

        public static AttemptDecision Failed(FailureReason reason, string? detail)
        {
            return new AttemptDecision(SendStatus.Failed, reason, null, detail);
        }

        public static AttemptDecision Skipped(SkipReason reason)
        {
            return new AttemptDecision(SendStatus.Skipped, null, reason, null);
        }

        public string Describe()
        {
            if (FailureReason != null)
            {
                return FailureReason.Value.ToString();
            }

            if (SkipReason != null)
            {
                return SkipReason.Value.ToString();
            }

            return string.Empty;
        }
    }

    private sealed record RecordedAttempt(Guid Id, bool IsAlreadySent, AttemptDecision Decision);

    // The entry an email was handed to the email service for, and what it is.
    private sealed record SendingEntry(Guid EntryId, ReceivedEvent Event, EmailType EmailType);

    private sealed record ExistingEntryRow(Guid Id, SendStatus Status);
}
