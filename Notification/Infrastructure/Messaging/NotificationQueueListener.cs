using Amazon.SQS;
using Amazon.SQS.Model;

namespace Notification.Infrastructure.Messaging;

/// <summary>
/// Reads the notification queue and hands each event to its slice through the dispatcher. A message
/// whose event cannot be used — not an event at all, or an event type with no email — is left for
/// the dead-letter queue, as is one that must be tried again.
/// </summary>
public sealed class NotificationQueueListener : SqsQueueListener
{
    private readonly NotificationEventDispatcher _dispatcher;
    private readonly ILogger<NotificationQueueListener> _logger;

    public NotificationQueueListener(
        IAmazonSQS sqs,
        IConfiguration configuration,
        NotificationEventDispatcher dispatcher,
        ILogger<NotificationQueueListener> logger)
        : base(sqs, configuration[ConfigKeys.NotificationQueueUrl]!, logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<EventHandlingOutcome?> HandleMessageAsync(
        Message message, bool isLastAttempt, CancellationToken cancellationToken)
    {
        EventEnvelopeReading reading = EventEnvelopeReader.Read(message.Body, isLastAttempt);
        if (reading.Event is null)
        {
            _logger.LogError(
                "Queue message {MessageId} cannot be used ({Problem}); left for the dead-letter queue",
                message.MessageId, reading.Problem);
            return null;
        }

        return await DispatchSafelyAsync(message, reading.Event, cancellationToken);
    }

    // A failure inside one email (the database briefly down, for instance) must not stop the
    // listener: the message stays on the queue and is delivered again.
    private async Task<EventHandlingOutcome?> DispatchSafelyAsync(
        Message message, ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        try
        {
            EventHandlingOutcome? outcome = await _dispatcher.DispatchAsync(receivedEvent, cancellationToken);
            if (outcome is null)
            {
                _logger.LogError(
                    "Queue message {MessageId} carries event type {EventType}, which has no email; left for the dead-letter queue",
                    message.MessageId, receivedEvent.EventType);
            }

            return outcome;
        }
        catch (DbException exception)
        {
            _logger.LogError(
                exception, "Event {EventId} (correlation {CorrelationId}) could not be recorded; it will be delivered again",
                receivedEvent.EventId, receivedEvent.CorrelationId);
            return EventHandlingOutcome.RetryLater;
        }
    }
}
