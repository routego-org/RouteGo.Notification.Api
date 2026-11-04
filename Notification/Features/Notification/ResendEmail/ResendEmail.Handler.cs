namespace Notification.Features.Notification.ResendEmail;

/// <summary>
/// Sends the email of one send log entry again — same recipient, subject and message, from the
/// current sender address — and records the attempt as a new entry pointing at the resent one. The
/// resent entry is never changed. A resend is sent once, immediately; one that fails is recorded as
/// Failed and is not retried on its own.
/// </summary>
public sealed partial class ResendEmailHandler
{
    private const string WriteAction = "ResendEmail";
    private const int DefaultLinkLifetimeHours = 24;

    private readonly EmailWorkerPorts _ports;
    private readonly TimeSpan _linkLifetime;
    private readonly ILogger<ResendEmailHandler> _logger;

    public ResendEmailHandler(EmailWorkerPorts ports, IConfiguration configuration, ILogger<ResendEmailHandler> logger)
    {
        _ports = ports;
        _linkLifetime = ReadLinkLifetime(configuration);
        _logger = logger;
    }

    /// <summary>Resends the entry, or says why it cannot be resent.</summary>
    public async Task<ResendEmailResult> HandleAsync(ResendEmailRequest request, CancellationToken cancellationToken)
    {
        var originalId = Guid.Parse(request.SendLogEntryId!);

        ResendAttempt attempt = await RecordAttemptAsync(originalId, cancellationToken);
        if (attempt.Refusal != null)
        {
            return attempt.Refusal;
        }

        OriginalEntryRow original = attempt.Original!;
        var message = new EmailMessage(original.Subject!, original.BodyHtml!, original.BodyText!);
        EmailSendResult sendResult = await _ports.EmailSender.SendAsync(original.ToAddress!, message, cancellationToken);
        return await RecordResultAsync(attempt, sendResult, cancellationToken);
    }

    // Loading the entry and recording the new attempt share one transaction, so the new entry is
    // written only for an entry that existed and passed every rule. The email service is called
    // after it commits: no database connection is held while it answers.
    private async Task<ResendAttempt> RecordAttemptAsync(Guid originalId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _ports.Clock.UtcNow;
        await using IWriterConnection writer =
            await _ports.Writer.OpenWriteTransactionAsync(WriteAction, cancellationToken);

        OriginalEntryRow? original = await writer.Connection.QuerySingleOrDefaultAsync<OriginalEntryRow>(
            new CommandDefinition(LoadOriginalSql, new { Id = originalId }, writer.Transaction, cancellationToken: cancellationToken));
        if (original is null)
        {
            return ResendAttempt.Refused(new ResendEmailResult { Outcome = ResendEmailOutcome.NotFound });
        }

        ResendEmailResult? refusal = await RefuseAsync(writer, original, now, cancellationToken);
        if (refusal != null)
        {
            return ResendAttempt.Refused(refusal);
        }

        var parameters = new { OriginalId = originalId, Status = WireName.Of(SendStatus.Sending), AttemptedAt = now };
        Guid newEntryId = await writer.Connection.QuerySingleAsync<Guid>(new CommandDefinition(
            RecordResendSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
        return ResendAttempt.Recorded(original, newEntryId);
    }

    private async Task<ResendEmailResult> RecordResultAsync(
        ResendAttempt attempt, EmailSendResult sendResult, CancellationToken cancellationToken)
    {
        switch (sendResult.Outcome)
        {
            case EmailSendOutcome.Accepted:
                return await RecordSentAsync(attempt, sendResult.ProviderMessageId!, cancellationToken);
            case EmailSendOutcome.Refused:
                await RecordFailedAsync(attempt, FailureReason.RefusedByEmailService, sendResult.Detail, cancellationToken);
                return new ResendEmailResult
                {
                    Outcome = ResendEmailOutcome.Refused,
                    NewEntryId = attempt.NewEntryId,
                    ProviderReason = sendResult.Detail,
                };
            case EmailSendOutcome.Unavailable:
                await RecordFailedAsync(attempt, FailureReason.EmailServiceUnavailable, sendResult.Detail, cancellationToken);
                return new ResendEmailResult
                {
                    Outcome = ResendEmailOutcome.EmailServiceUnavailable,
                    NewEntryId = attempt.NewEntryId,
                };
            default:
                throw new InvalidOperationException($"Unhandled email send outcome '{sendResult.Outcome}'.");
        }
    }

    private async Task<ResendEmailResult> RecordSentAsync(
        ResendAttempt attempt, string providerMessageId, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            Id = attempt.NewEntryId,
            Status = WireName.Of(SendStatus.Sent),
            ProviderMessageId = providerMessageId,
            SentAt = _ports.Clock.UtcNow,
        };
        await using IWriterConnection writer =
            await _ports.Writer.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        EntryRow sent = await writer.Connection.QuerySingleAsync<EntryRow>(new CommandDefinition(
            MarkSentSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);

        OriginalEntryRow original = attempt.Original!;
        _logger.LogInformation(
            "Resend {SendLogEntryId} of entry {ResendOf} for event {EventId} ({EmailType}, correlation {CorrelationId}) sent",
            attempt.NewEntryId, original.Id, original.EventId, original.EmailType, original.CorrelationId);
        return new ResendEmailResult { Outcome = ResendEmailOutcome.Sent, Response = Map(sent) };
    }

    private async Task RecordFailedAsync(
        ResendAttempt attempt, FailureReason reason, string? detail, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            Id = attempt.NewEntryId,
            Status = WireName.Of(SendStatus.Failed),
            FailureReason = WireName.Of(reason),
            FailureDetail = detail,
        };
        await using IWriterConnection writer =
            await _ports.Writer.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            MarkFailedSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);

        OriginalEntryRow original = attempt.Original!;
        _logger.LogWarning(
            "Resend {SendLogEntryId} of entry {ResendOf} for event {EventId} ({EmailType}, correlation {CorrelationId}) failed: {FailureReason}",
            attempt.NewEntryId, original.Id, original.EventId, original.EmailType, original.CorrelationId, reason);
    }

    // A lifetime that is not a positive whole number of hours would refuse or allow resends on a
    // wrong promise, so it fails the request loudly, naming the setting to fix.
    private static TimeSpan ReadLinkLifetime(IConfiguration configuration)
    {
        string? text = configuration[Infrastructure.ConfigKeys.VerificationLinkLifetimeHours];
        if (string.IsNullOrWhiteSpace(text))
        {
            return TimeSpan.FromHours(DefaultLinkLifetimeHours);
        }

        bool isNumber = int.TryParse(text, out int hours);
        if (!isNumber || hours < 1)
        {
            throw new InvalidOperationException(
                $"{Infrastructure.ConfigKeys.VerificationLinkLifetimeHours} must be a whole number of hours, 1 or more; it is '{text}'.");
        }

        return TimeSpan.FromHours(hours);
    }

    private static ResendEmailResponse Map(EntryRow row)
    {
        DateTimeOffset? sentAt = null;
        if (row.SentAt != null)
        {
            sentAt = AsUtc(row.SentAt.Value);
        }

        DateTimeOffset? departsAt = null;
        if (row.DepartsAt != null)
        {
            departsAt = AsUtc(row.DepartsAt.Value);
        }

        return new ResendEmailResponse
        {
            SendLogEntryId = row.Id,
            EventId = row.EventId,
            EventType = row.EventType,
            EmailType = row.EmailType,
            BookingId = row.BookingId,
            DepartsAt = departsAt,
            ToAddress = row.ToAddress,
            Subject = row.Subject,
            Status = row.Status,
            FailureReason = row.FailureReason,
            SkipReason = row.SkipReason,
            FailureDetail = row.FailureDetail,
            ProviderMessageId = row.ProviderMessageId,
            ResendOf = row.ResendOf,
            EventOccurredAt = AsUtc(row.EventOccurredAt),
            CorrelationId = row.CorrelationId,
            AttemptedAt = AsUtc(row.AttemptedAt),
            SentAt = sentAt,
        };
    }

    // The driver reads timestamptz as a UTC DateTime; the contract's times carry their offset.
    private static DateTimeOffset AsUtc(DateTime utcTime)
    {
        return new DateTimeOffset(utcTime, TimeSpan.Zero);
    }

    // Either a refusal decided before anything was recorded, or the entry being resent and the new
    // entry recorded for this attempt.
    private sealed record ResendAttempt(ResendEmailResult? Refusal, OriginalEntryRow? Original, Guid NewEntryId)
    {
        public static ResendAttempt Refused(ResendEmailResult refusal)
        {
            return new ResendAttempt(refusal, null, Guid.Empty);
        }

        public static ResendAttempt Recorded(OriginalEntryRow original, Guid newEntryId)
        {
            return new ResendAttempt(null, original, newEntryId);
        }
    }
}
