namespace Notification.Tests.Support;

/// <summary>A clock that always reads the time it was given, so recorded times are predictable.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; }
}
