using Notification.Features.Notification.SendDepartureReminderEmail;

namespace Notification.Infrastructure.Reminders;

/// <summary>
/// Fires each booking's planned departure reminder when its time comes. Once a minute it asks the
/// known bookings for the planned reminders that are due — an indexed lookup of due reminders, never
/// a scan of all bookings — and sends each. A reminder that failed temporarily stays planned and is
/// tried again on the next minute, until its last attempt parks it.
/// </summary>
public sealed class DepartureReminderScheduler : BackgroundService
{
    // A reminder is worth sending as long as the bus has not left, but a failing email service must
    // not be hammered forever: five tries, a minute apart, then the reminder is parked and alerts.
    // The count lives in memory, so a restart gives a stuck reminder a fresh five tries.
    private const int MaxAttempts = 5;
    private const int BatchSize = 50;

    private static readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(1);

    private readonly SendDepartureReminderEmailHandler _handler;
    private readonly KnownBookingStore _knownBookings;
    private readonly IClock _clock;
    private readonly ILogger<DepartureReminderScheduler> _logger;
    private readonly Dictionary<Guid, int> _failedAttempts = new Dictionary<Guid, int>();

    public DepartureReminderScheduler(
        SendDepartureReminderEmailHandler handler,
        KnownBookingStore knownBookings,
        IClock clock,
        ILogger<DepartureReminderScheduler> logger)
    {
        _handler = handler;
        _knownBookings = knownBookings;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunDueRemindersSafelyAsync(stoppingToken);
            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    /// <summary>Sends every reminder that is due now, once each, and returns how many were handled for good.</summary>
    public async Task<int> RunDueRemindersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> due = await _knownBookings.ListDueRemindersAsync(_clock.UtcNow, BatchSize, cancellationToken);
        int handled = 0;
        foreach (Guid bookingId in due)
        {
            bool isDone = await HandleReminderAsync(bookingId, cancellationToken);
            if (isDone)
            {
                handled++;
            }
        }

        return handled;
    }

    // A failure inside one reminder (the database briefly down, for instance) must not stop the
    // scheduler: the reminder stays planned and is found again on the next minute.
    private async Task RunDueRemindersSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunDueRemindersAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            _logger.LogError(exception, "Departure reminders could not be read or recorded; retrying in a minute");
        }
    }

    private async Task<bool> HandleReminderAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        _failedAttempts.TryGetValue(bookingId, out int failedSoFar);
        bool isLastAttempt = failedSoFar + 1 >= MaxAttempts;
        EventHandlingOutcome outcome = await _handler.HandleAsync(bookingId, isLastAttempt, cancellationToken);
        if (outcome == EventHandlingOutcome.Consumed || isLastAttempt)
        {
            _failedAttempts.Remove(bookingId);
            return true;
        }

        _failedAttempts[bookingId] = failedSoFar + 1;
        return false;
    }
}
