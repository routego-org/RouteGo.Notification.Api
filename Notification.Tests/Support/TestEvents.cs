using System.Text.Json;

namespace Notification.Tests.Support;

/// <summary>Builds the events the email slices receive, the way the queue listener hands them over.</summary>
public static class TestEvents
{
    /// <summary>The time every test event occurred, unless a test says otherwise.</summary>
    public static readonly DateTimeOffset OccurredAt = new DateTimeOffset(2026, 10, 2, 10, 14, 0, TimeSpan.Zero);

    /// <summary>An event of <paramref name="eventType"/> whose data is <paramref name="data"/>, serialized as JSON.</summary>
    public static ReceivedEvent Create(string eventType, string eventId, object data, bool isLastAttempt = false)
    {
        JsonElement json = JsonSerializer.SerializeToElement(data);
        return new ReceivedEvent
        {
            EventId = eventId,
            EventType = eventType,
            OccurredAt = OccurredAt,
            CorrelationId = $"corr-{eventId}",
            Data = json,
            IsLastAttempt = isLastAttempt,
        };
    }
}
