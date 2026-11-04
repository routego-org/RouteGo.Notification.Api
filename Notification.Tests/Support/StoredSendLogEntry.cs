namespace Notification.Tests.Support;

/// <summary>
/// A send log entry a test puts straight into the database. Defaults to a Sent customer
/// verification email to ravi@gmail.com; a test changes only what its criterion is about, with
/// <c>with</c>, and starts from <see cref="Failed"/> for an entry that failed.
/// </summary>
public sealed record StoredSendLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EventId { get; init; } = "E1";
    public string EventType { get; init; } = "userRegistered";
    public string EmailType { get; init; } = "customerVerification";
    private readonly string? _repeatKey;

    /// <summary>What makes this the same email on a repeat delivery; the event ID unless a test sets it.</summary>
    public string RepeatKey
    {
        get { return _repeatKey ?? EventId; }
        init { _repeatKey = value; }
    }

    public Guid? BookingId { get; init; }
    public DateTimeOffset? DepartsAt { get; init; }
    public string? ToAddress { get; init; } = "ravi@gmail.com";
    public string? Subject { get; init; } = "Verify your email for RouteGo";
    public string? BodyHtml { get; init; } = "<p>Hi Ravi Kumar,</p>";
    public string? BodyText { get; init; } = "Hi Ravi Kumar,";
    public string Status { get; init; } = "sent";
    public string? FailureReason { get; init; }
    public string? SkipReason { get; init; }
    public string? FailureDetail { get; init; }
    public string? ProviderMessageId { get; init; } = "ses-original";
    public Guid? ResendOf { get; init; }
    public DateTimeOffset EventOccurredAt { get; init; } = TestEvents.OccurredAt;
    public string CorrelationId { get; init; } = "corr-E1";
    public DateTimeOffset AttemptedAt { get; init; } = TestEvents.OccurredAt;
    public DateTimeOffset? SentAt { get; init; } = TestEvents.OccurredAt;

    /// <summary>The default entry, failed for <paramref name="failureReason"/>: no receipt number and no sent time.</summary>
    public static StoredSendLogEntry Failed(string failureReason)
    {
        return new StoredSendLogEntry
        {
            Status = "failed",
            FailureReason = failureReason,
            ProviderMessageId = null,
            SentAt = null,
        };
    }
}
