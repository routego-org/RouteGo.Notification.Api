namespace Notification.Tests.Support;

/// <summary>One send log entry as stored, read straight from the database by a test.</summary>
public sealed record SendLogEntryRow
{
    public Guid Id { get; init; }
    public string EventId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string EmailType { get; init; } = string.Empty;
    public string RepeatKey { get; init; } = string.Empty;
    public Guid? BookingId { get; init; }
    public DateTime? DepartsAt { get; init; }
    public string? ToAddress { get; init; }
    public string? Subject { get; init; }
    public string? BodyHtml { get; init; }
    public string? BodyText { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public string? SkipReason { get; init; }
    public string? FailureDetail { get; init; }
    public string? ProviderMessageId { get; init; }
    public Guid? ResendOf { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime EventOccurredAt { get; init; }
    public DateTime AttemptedAt { get; init; }
    public DateTime? SentAt { get; init; }
}
