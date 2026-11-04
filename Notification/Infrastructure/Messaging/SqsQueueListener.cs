using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Notification.Infrastructure.Messaging;

/// <summary>
/// Reads one queue for as long as the process runs and hands each message to a subclass, one at a
/// time. A message the subclass says is handled is deleted; one that must be tried again — or that
/// cannot be used at all — is left, so the queue delivers it again after its visibility timeout and,
/// once its receive limit is reached, parks it on the dead-letter queue, whose alarm tells the
/// platform engineer.
/// </summary>
public abstract class SqsQueueListener : BackgroundService
{
    private const int MaxMessagesPerReceive = 10;
    private const int LongPollSeconds = 20;
    private const string ReceiveCountAttribute = "ApproximateReceiveCount";
    private const string RedrivePolicyAttribute = "RedrivePolicy";

    // How long to wait before trying the queue again after it could not be reached, so an outage
    // is retried steadily instead of in a tight loop.
    private static readonly TimeSpan _queueUnreachableDelay = TimeSpan.FromSeconds(10);

    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;
    private readonly ILogger _logger;

    protected SqsQueueListener(IAmazonSQS sqs, string queueUrl, ILogger logger)
    {
        _sqs = sqs;
        _queueUrl = queueUrl;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int? maxReceiveCount = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                maxReceiveCount ??= await ReadMaxReceiveCountAsync(stoppingToken);
                await ReceiveAndHandleAsync(maxReceiveCount.Value, stoppingToken);
            }
            catch (AmazonSQSException exception)
            {
                _logger.LogError(exception, "Queue {QueueUrl} could not be read; retrying", _queueUrl);
                await Task.Delay(_queueUnreachableDelay, stoppingToken);
            }
            catch (AmazonClientException exception)
            {
                _logger.LogError(exception, "Queue {QueueUrl} could not be reached; retrying", _queueUrl);
                await Task.Delay(_queueUnreachableDelay, stoppingToken);
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(exception, "Queue {QueueUrl} could not be reached; retrying", _queueUrl);
                await Task.Delay(_queueUnreachableDelay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Handles one message. Returns <see cref="EventHandlingOutcome.Consumed"/> to delete it,
    /// <see cref="EventHandlingOutcome.RetryLater"/> to have it delivered again, or null for a message
    /// that cannot be used at all, which is left for the dead-letter queue.
    /// </summary>
    /// <param name="message">The queue message.</param>
    /// <param name="isLastAttempt">Whether the queue parks this message if it is not deleted now.</param>
    /// <param name="cancellationToken">Cancelled when the process stops.</param>
    protected abstract Task<EventHandlingOutcome?> HandleMessageAsync(
        Message message, bool isLastAttempt, CancellationToken cancellationToken);

    // The receive limit is read from the queue's own redrive policy, so the last attempt this
    // process acts on is always the one the queue enforces.
    private async Task<int> ReadMaxReceiveCountAsync(CancellationToken cancellationToken)
    {
        var request = new GetQueueAttributesRequest
        {
            QueueUrl = _queueUrl,
            AttributeNames = new List<string> { RedrivePolicyAttribute },
        };
        GetQueueAttributesResponse response = await _sqs.GetQueueAttributesAsync(request, cancellationToken);
        string? policy = null;
        if (response.Attributes != null)
        {
            response.Attributes.TryGetValue(RedrivePolicyAttribute, out policy);
        }

        int? maxReceiveCount = RedrivePolicy.ReadMaxReceiveCount(policy);
        if (maxReceiveCount is null)
        {
            throw new InvalidOperationException(
                $"Queue {_queueUrl} has no dead-letter redrive policy with a maxReceiveCount; "
                + "without it a failing message is retried forever and never parked.");
        }

        return maxReceiveCount.Value;
    }

    private async Task ReceiveAndHandleAsync(int maxReceiveCount, CancellationToken cancellationToken)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = MaxMessagesPerReceive,
            WaitTimeSeconds = LongPollSeconds,
            MessageSystemAttributeNames = new List<string> { ReceiveCountAttribute },
        };
        ReceiveMessageResponse response = await _sqs.ReceiveMessageAsync(request, cancellationToken);
        if (response.Messages is null)
        {
            return;
        }

        foreach (Message message in response.Messages)
        {
            bool isLastAttempt = SendLogRules.IsLastAttempt(ReadReceiveCount(message), maxReceiveCount);
            EventHandlingOutcome? outcome = await HandleMessageAsync(message, isLastAttempt, cancellationToken);
            if (outcome == EventHandlingOutcome.Consumed)
            {
                await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, cancellationToken);
            }
        }
    }

    private static int ReadReceiveCount(Message message)
    {
        string? text = null;
        if (message.Attributes != null)
        {
            message.Attributes.TryGetValue(ReceiveCountAttribute, out text);
        }

        bool isNumber = int.TryParse(text, out int receiveCount);
        if (!isNumber)
        {
            return 1;
        }

        return receiveCount;
    }
}
