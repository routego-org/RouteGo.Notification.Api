namespace Notification.Features.Notification.RecordEmailServiceReport;

/// <summary>
/// Puts every address the email service reports as permanently bouncing, or as having complained,
/// on the do-not-send list. A temporary bounce changes nothing, an address already on the list is
/// left as it is, and a report that cannot be recorded is retried and, after repeated failure,
/// parked with an alert.
/// </summary>
public sealed class RecordEmailServiceReportHandler
{
    private const string WriteAction = "RecordEmailServiceReport";

    // One statement for every address of the report: a repeated address, in any case, changes nothing.
    private const string SuppressAddressesSql = """
        INSERT INTO notification.suppressed_addresses
            (email_address, reason, provider_message_id, report_detail, reported_at, suppressed_at)
        SELECT address, @Reason, @ProviderMessageId, @ReportDetail, @ReportedAt, @SuppressedAt
        FROM unnest(@Addresses) AS address
        ON CONFLICT (lower(email_address)) DO NOTHING;
        """;

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly IClock _clock;
    private readonly ILogger<RecordEmailServiceReportHandler> _logger;

    public RecordEmailServiceReportHandler(
        IWriterConnectionFactory writerFactory, IClock clock, ILogger<RecordEmailServiceReportHandler> logger)
    {
        _writerFactory = writerFactory;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Records one report and says whether the message is done with or must be delivered again.
    /// <paramref name="isLastAttempt"/> says that a failure to record now parks the report.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(
        EmailServiceReport report, bool isLastAttempt, CancellationToken cancellationToken)
    {
        if (report.Kind == ReportKind.TemporaryBounce)
        {
            _logger.LogInformation("A temporary bounce report for {AddressCount} address(es) changes nothing", report.Addresses.Count);
            return EventHandlingOutcome.Consumed;
        }

        try
        {
            await SuppressAsync(report, cancellationToken);
        }
        catch (DbException exception)
        {
            LogFailure(report, isLastAttempt, exception);
            return EventHandlingOutcome.RetryLater;
        }

        _logger.LogInformation(
            "{ReportKind} report for {AddressCount} address(es) recorded on the do-not-send list",
            report.Kind, report.Addresses.Count);
        return EventHandlingOutcome.Consumed;
    }

    private async Task SuppressAsync(EmailServiceReport report, CancellationToken cancellationToken)
    {
        SuppressionReason reason = SuppressionReason.PermanentBounce;
        if (report.Kind == ReportKind.Complaint)
        {
            reason = SuppressionReason.Complaint;
        }

        var parameters = new
        {
            Reason = WireName.Of(reason),
            report.ProviderMessageId,
            ReportDetail = report.Detail,
            report.ReportedAt,
            SuppressedAt = _clock.UtcNow,
            Addresses = report.Addresses.ToArray(),
        };
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            SuppressAddressesSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
    }

    // The report is named by its kind, how many addresses it has, its time and the receipt number
    // of the reported email — never by the addresses, which are personal data. On the last attempt
    // the alert is raised here; the message itself is left on the queue, which then moves it to the
    // parked-messages queue for a replay after the fix.
    private void LogFailure(EmailServiceReport report, bool isLastAttempt, DbException exception)
    {
        if (!isLastAttempt)
        {
            _logger.LogWarning(
                exception, "{ReportKind} report for {AddressCount} address(es) not recorded yet, will retry",
                report.Kind, report.Addresses.Count);
            return;
        }

        _logger.LogError(
            exception,
            "{ProblemCode}: {ReportKind} report for {AddressCount} address(es) at {ReportedAt} (email {ProviderMessageId}) parked after repeated failure",
            ProblemTypes.EmailReportParked.Code, report.Kind, report.Addresses.Count, report.ReportedAt,
            report.ProviderMessageId);
    }
}
