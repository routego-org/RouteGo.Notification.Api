namespace Notification.Domain;

/// <summary>What the queue listener does with a message once an email slice has handled its event.</summary>
public enum EventHandlingOutcome
{
    /// <summary>Done with: the message is deleted from the queue.</summary>
    Consumed,

    /// <summary>Left on the queue, so it is delivered again later — or parked after the last attempt.</summary>
    RetryLater,
}
