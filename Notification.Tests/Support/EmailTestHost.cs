namespace Notification.Tests.Support;

/// <summary>
/// What a booking email slice's test needs around its handler: the email service it can script,
/// the recorder over the fixture's database with a log it can read, the known bookings and the
/// Portal links. The clock stands at <see cref="Now"/>.
/// </summary>
public sealed class EmailTestHost
{
    /// <summary>The time the clock reads in every test: 5 Nov 2026 04:30 UTC, ten minutes after the default payment.</summary>
    public static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 5, 4, 30, 0, TimeSpan.Zero);

    public EmailTestHost(DatabaseFixture database, FakeEmailSender emails, DateTimeOffset? now = null)
    {
        Clock = new FixedClock(now ?? Now);
        Emails = emails;
        Log = new FakeLogger<EmailSendRecorder>();
        var ports = new EmailWorkerPorts(SendLogStore.Writer(database), emails, Clock);
        Recorder = new EmailSendRecorder(ports, Log);
        KnownBookings = new KnownBookingStore(ports.Writer);
        Links = new PortalLinks(new Uri("https://portal.routego.in/"), new Uri("https://portal.routego.in/auth/login"));
    }

    public FakeEmailSender Emails { get; }

    public FixedClock Clock { get; }

    public FakeLogger<EmailSendRecorder> Log { get; }

    public EmailSendRecorder Recorder { get; }

    public KnownBookingStore KnownBookings { get; }

    public PortalLinks Links { get; }

    /// <summary>The alert line a parked email raises, or null when none was raised.</summary>
    public FakeLogRecord? ParkedAlert()
    {
        return Log.Collector.GetSnapshot().SingleOrDefault(record => record.Level == LogLevel.Error);
    }

    /// <summary>Whether anything at warning level or above was logged, which a dropped email never does.</summary>
    public bool RaisedWarningOrAlert()
    {
        return Log.Collector.GetSnapshot().Any(record => record.Level >= LogLevel.Warning);
    }
}
