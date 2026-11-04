namespace Notification.Tests.Support;

/// <summary>
/// An RFC 7807 problem response as a test reads it: the standard members, the cataloged code, and
/// every extension member the internal support operations can add.
/// </summary>
public sealed record ProblemBody
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public int? Status { get; init; }
    public string? Detail { get; init; }
    public string? Code { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
    public string? FailureReason { get; init; }
    public DateTimeOffset? LinkExpiredAt { get; init; }
    public Guid? SendLogEntryId { get; init; }
    public string? ProviderReason { get; init; }
    public bool? Retryable { get; init; }
    public Guid? BookingId { get; init; }
    public DateTimeOffset? DepartsAt { get; init; }
    public string? ToAddress { get; init; }
    public string? SuppressionReason { get; init; }
}
