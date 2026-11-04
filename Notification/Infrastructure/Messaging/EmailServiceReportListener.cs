using Amazon.SQS;
using Amazon.SQS.Model;
using Notification.Features.Notification.RecordEmailServiceReport;

namespace Notification.Infrastructure.Messaging;

/// <summary>
/// Reads the queue the email service's bounce and complaint reports arrive on and hands each to the
/// do-not-send list. A report that names no address or is of a kind the list does not use is
/// dropped without an alert; one that cannot be recorded stays on the queue to be retried and
/// parked.
/// </summary>
public sealed class EmailServiceReportListener : SqsQueueListener
{
    private readonly RecordEmailServiceReportHandler _handler;
    private readonly ILogger<EmailServiceReportListener> _logger;

    public EmailServiceReportListener(
        IAmazonSQS sqs,
        IConfiguration configuration,
        RecordEmailServiceReportHandler handler,
        ILogger<EmailServiceReportListener> logger)
        : base(sqs, configuration[ConfigKeys.EmailServiceReportsQueueUrl]!, logger)
    {
        _handler = handler;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<EventHandlingOutcome?> HandleMessageAsync(
        Message message, bool isLastAttempt, CancellationToken cancellationToken)
    {
        EmailServiceReportReading reading = EmailServiceReportReader.Read(message.Body);
        if (reading.Report is null)
        {
            _logger.LogInformation(
                "Report message {MessageId} is not used and is dropped: {Problem}", message.MessageId, reading.Problem);
            return EventHandlingOutcome.Consumed;
        }

        return await _handler.HandleAsync(reading.Report, isLastAttempt, cancellationToken);
    }
}
