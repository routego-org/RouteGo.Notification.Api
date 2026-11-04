using Microsoft.Extensions.Configuration;
using Notification.Features.Notification.PlanDepartureReminder;

namespace Notification.Infrastructure.Messaging;

[Collection(DatabaseCollection.Name)]
public sealed class NotificationEventDispatcherTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;

    public NotificationEventDispatcherTests(DatabaseFixture database)
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
    public async Task DispatchAsync_BookingConfirmed_SendsTheCustomersAndTheOperatorsEmail()
    {
        var emails = new FakeEmailSender();
        NotificationEventDispatcher dispatcher = NewDispatcher(emails);
        ReceivedEvent confirmed = TestEvents.Create("BookingConfirmed", "E20", new TestBooking().Data());

        EventHandlingOutcome? outcome = await dispatcher.DispatchAsync(confirmed, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Equal(2, emails.Sent.Count);
        Assert.Contains(emails.Sent, email => email.ToAddress == "ravi@gmail.com");
        Assert.Contains(emails.Sent, email => email.ToAddress == "bookings@busco.in");
    }

    [Fact]
    public async Task DispatchAsync_BookingConfirmedWhileEmailServiceIsDown_RetriesTheWholeMessage()
    {
        var emails = new FakeEmailSender(EmailSendResult.Unavailable("down"), EmailSendResult.Accepted("ses-2"));
        NotificationEventDispatcher dispatcher = NewDispatcher(emails);
        ReceivedEvent confirmed = TestEvents.Create("BookingConfirmed", "E20", new TestBooking().Data());

        EventHandlingOutcome? outcome = await dispatcher.DispatchAsync(confirmed, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        Assert.Equal(2, emails.Sent.Count);
    }

    [Fact]
    public async Task DispatchAsync_EventTypeWithNoEmail_ReturnsNull()
    {
        NotificationEventDispatcher dispatcher = NewDispatcher(new FakeEmailSender());
        ReceivedEvent unknown = TestEvents.Create("AuditRecorded", "E1", new { });

        EventHandlingOutcome? outcome = await dispatcher.DispatchAsync(unknown, CancellationToken.None);

        Assert.Null(outcome);
    }

    private NotificationEventDispatcher NewDispatcher(FakeEmailSender emails)
    {
        var ports = new EmailWorkerPorts(SendLogStore.Writer(_database), emails, new FixedClock(EmailTestHost.Now));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.PortalBaseUrl] = "https://portal.routego.in",
                [ConfigKeys.PortalSignInPath] = "/auth/login",
            })
            .Build();
        var knownBookings = new KnownBookingStore(ports.Writer);
        var planner = new PlanDepartureReminderHandler(
            knownBookings, ports.Clock, new FakeLogger<PlanDepartureReminderHandler>());
        var recorder = new EmailSendRecorder(ports, new FakeLogger<EmailSendRecorder>());
        return new NotificationEventDispatcher(
            recorder, knownBookings, NotificationSettings.FromConfiguration(configuration), planner);
    }
}
