using Notification.Features.Notification.SendDepartureReminderEmail;

namespace Notification.Infrastructure.Reminders;

[Collection(DatabaseCollection.Name)]
public sealed class DepartureReminderSchedulerTests : IAsyncLifetime
{
    private static readonly Guid _otherBookingId = Guid.Parse("7d1f0a52-3c4b-4e8a-9f2d-1b6c5e8a9d01");

    private readonly DatabaseFixture _database;

    public DepartureReminderSchedulerTests(DatabaseFixture database)
    {
        _database = database;
    }

    public async Task InitializeAsync()
    {
        await _database.ResetBusinessTablesAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RunDueRemindersAsync_OneDueOneNot_SendsOnlyTheDueReminder()
    {
        DateTimeOffset dueDeparture = EmailTestHost.Now.AddHours(20);
        DateTimeOffset laterDeparture = EmailTestHost.Now.AddDays(5);
        await KnownBookingFixtures.InsertAsync(
            _database, StoredKnownBooking.WithReminderDue(TestBooking.DefaultId, dueDeparture, EmailTestHost.Now.AddHours(-4)));
        await KnownBookingFixtures.InsertAsync(
            _database, StoredKnownBooking.WithReminderDue(_otherBookingId, laterDeparture, laterDeparture.AddHours(-24)) with { ConfirmedEventId = "E21" });
        EmailTestHost host = NewHost();
        DepartureReminderScheduler scheduler = NewScheduler(host);

        int handled = await scheduler.RunDueRemindersAsync(CancellationToken.None);

        Assert.Equal(1, handled);
        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal(0, await scheduler.RunDueRemindersAsync(CancellationToken.None));
        Assert.Single(host.Emails.Sent);
    }

    [Fact]
    public async Task RunDueRemindersAsync_TemporaryFailure_TriesAgainOnTheNextRun()
    {
        await SeedDueReminderAsync();
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));
        DepartureReminderScheduler scheduler = NewScheduler(host);

        int firstRun = await scheduler.RunDueRemindersAsync(CancellationToken.None);
        int secondRun = await scheduler.RunDueRemindersAsync(CancellationToken.None);

        Assert.Equal(0, firstRun);
        Assert.Equal(1, secondRun);
        Assert.Equal(2, host.Emails.Sent.Count);
        SendLogEntryRow entry = Assert.Single(await SendLogStore.EntriesForEventAsync(_database, "E20"));
        Assert.Equal("sent", entry.Status);
    }

    [Fact]
    public async Task RunDueRemindersAsync_FifthFailureInARow_ParksTheReminderAndRaisesTheAlert()
    {
        await SeedDueReminderAsync();
        var down = EmailSendResult.Unavailable("ServiceUnavailable: try later");
        EmailTestHost host = NewHost(down, down, down, down, down);
        DepartureReminderScheduler scheduler = NewScheduler(host);

        for (int run = 1; run <= 5; run++)
        {
            await scheduler.RunDueRemindersAsync(CancellationToken.None);
        }

        SendLogEntryRow entry = Assert.Single(await SendLogStore.EntriesForEventAsync(_database, "E20"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        Assert.NotNull(host.ParkedAlert());
        Assert.Equal(0, await scheduler.RunDueRemindersAsync(CancellationToken.None));
        Assert.Equal(5, host.Emails.Sent.Count);
    }

    private Task SeedDueReminderAsync()
    {
        DateTimeOffset departure = EmailTestHost.Now.AddHours(20);
        var booking = StoredKnownBooking.WithReminderDue(TestBooking.DefaultId, departure, EmailTestHost.Now.AddHours(-4));
        return KnownBookingFixtures.InsertAsync(_database, booking);
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static DepartureReminderScheduler NewScheduler(EmailTestHost host)
    {
        var handler = new SendDepartureReminderEmailHandler(host.Recorder, host.KnownBookings, host.Links);
        return new DepartureReminderScheduler(
            handler, host.KnownBookings, host.Clock, new FakeLogger<DepartureReminderScheduler>());
    }
}
