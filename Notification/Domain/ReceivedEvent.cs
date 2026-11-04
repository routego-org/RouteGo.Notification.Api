namespace Notification.Domain;

/// <summary>
/// One event taken off the notification queue, as an email slice sees it: the envelope fields it
/// records in the send log, the event's own data, and whether this is the queue's last automatic
/// attempt before the message is parked.
/// </summary>
public sealed record ReceivedEvent
{
    /// <summary>The event ID from the producer's envelope; the same on every repeat delivery.</summary>
    public required string EventId { get; init; }

    /// <summary>The producer's event type, for example <c>UserRegistered</c>.</summary>
    public required string EventType { get; init; }

    /// <summary>When the event occurred, in UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The correlation ID that ties this event to the request that caused it.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>The event-specific data object.</summary>
    public required JsonElement Data { get; init; }

    /// <summary>Whether a temporary failure now parks the email instead of retrying it.</summary>
    public required bool IsLastAttempt { get; init; }
}
