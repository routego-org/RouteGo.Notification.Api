namespace Notification.Features.Notification.PlanDepartureReminder;

[Collection(DatabaseCollection.Name)]
public sealed class PlanDepartureReminderTests : IAsyncLifetime
{
    private static readonly TimeSpan _leadTime = TimeSpan.FromHours(24);

    private readonly DatabaseFixture _database;

    public PlanDepartureReminderTests(DatabaseFixture database)
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
    public async Task HandleAsync_BookedDaysAhead_KnowsTheBookingAndPlansTheReminder24HoursBefore()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Confirmed("E20", new TestBooking()), _leadTime, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("confirmed", known.Status);
        Assert.Equal("ravi@gmail.com", known.ContactEmail);
        Assert.Equal("BusCo Volvo", known.BusName);
        Assert.Equal("12, 13", known.SeatLabels);
        Assert.Equal(0, known.SeatChangeCount);
        Assert.Equal("planned", known.ReminderState);
        Assert.Equal<DateTime?>(new DateTime(2026, 11, 11, 14, 30, 0, DateTimeKind.Utc), known.ReminderDueAt);
    }

    [Fact]
    public async Task HandleAsync_BookedLessThan24HoursBeforeDeparture_PlansNoReminder()
    {
        EmailTestHost host = NewHost();
        var soonBooking = new TestBooking { DepartureUtc = EmailTestHost.Now.AddHours(10) };

        await NewHandler(host).HandleAsync(Confirmed("E21", soonBooking), _leadTime, CancellationToken.None);

        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("notPlanned", known.ReminderState);
        Assert.Null(known.ReminderDueAt);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_KeepsOneReminderAtTheSameTime()
    {
        EmailTestHost host = NewHost();
        PlanDepartureReminderHandler handler = NewHandler(host);
        ReceivedEvent confirmed = Confirmed("E20", new TestBooking());
        await handler.HandleAsync(confirmed, _leadTime, CancellationToken.None);

        await handler.HandleAsync(confirmed, _leadTime, CancellationToken.None);

        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("planned", known.ReminderState);
        Assert.Equal<DateTime?>(new DateTime(2026, 11, 11, 14, 30, 0, DateTimeKind.Utc), known.ReminderDueAt);
    }

    [Fact]
    public async Task HandleAsync_BookingKnownToBeCancelled_PlansNoReminderAndStaysCancelled()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Confirmed("E22", new TestBooking()), _leadTime, CancellationToken.None);

        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("cancelled", known.Status);
        Assert.Equal("notPlanned", known.ReminderState);
        Assert.Null(known.ReminderDueAt);
    }

    [Fact]
    public async Task HandleAsync_BookingKnownFromASeatChange_KeepsTheNewerSeatsAndPlansTheReminder()
    {
        var changed = new StoredKnownBooking { SeatLabels = "13, 14", SeatChangeCount = 1 };
        await KnownBookingFixtures.InsertAsync(_database, changed);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Confirmed("E20", new TestBooking()), _leadTime, CancellationToken.None);

        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("13, 14", known.SeatLabels);
        Assert.Equal(1, known.SeatChangeCount);
        Assert.Equal("planned", known.ReminderState);
    }

    [Fact]
    public async Task HandleAsync_UnusableEventData_RecordsNothingAndRaisesReminderNotPlannedWithoutRetry()
    {
        EmailTestHost host = NewHost();
        var logger = new FakeLogger<PlanDepartureReminderHandler>();
        var handler = new PlanDepartureReminderHandler(host.KnownBookings, host.Clock, logger);
        var noSeats = new TestBooking { Seats = new List<TestSeat>() };

        EventHandlingOutcome outcome = await handler.HandleAsync(Confirmed("E23", noSeats), _leadTime, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Null(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        FakeLogRecord alert = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.Contains("REMINDER_NOT_PLANNED", alert.Message);
        Assert.Contains("invalidEventData", alert.Message);
        Assert.Contains("seats is missing", alert.Message);
    }

    [Fact]
    public async Task HandleAsync_PlanningFailsBeforeTheLastAttempt_RetriesWithoutAnAlert()
    {
        var logger = new FakeLogger<PlanDepartureReminderHandler>();
        PlanDepartureReminderHandler handler = NewFailingHandler(logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(Confirmed("E20", new TestBooking()), _leadTime, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
    }

    [Fact]
    public async Task HandleAsync_PlanningFailsOnTheLastAttempt_RaisesReminderNotPlannedAndReplayPlansIt()
    {
        var logger = new FakeLogger<PlanDepartureReminderHandler>();
        PlanDepartureReminderHandler failing = NewFailingHandler(logger);
        ReceivedEvent lastAttempt = Confirmed("E20", new TestBooking(), isLastAttempt: true);

        EventHandlingOutcome outcome = await failing.HandleAsync(lastAttempt, _leadTime, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        FakeLogRecord alert = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.Contains("REMINDER_NOT_PLANNED", alert.Message);
        Assert.Contains("E20", alert.Message);
        Assert.Contains(TestBooking.DefaultId.ToString(), alert.Message);
        Assert.Contains("repeatedFailure", alert.Message);

        EmailTestHost fixedHost = NewHost();
        await NewHandler(fixedHost).HandleAsync(Confirmed("E20", new TestBooking()), _leadTime, CancellationToken.None);
        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("planned", known.ReminderState);
    }

    private EmailTestHost NewHost()
    {
        return new EmailTestHost(_database, new FakeEmailSender());
    }

    private static PlanDepartureReminderHandler NewHandler(EmailTestHost host)
    {
        return new PlanDepartureReminderHandler(host.KnownBookings, host.Clock, new FakeLogger<PlanDepartureReminderHandler>());
    }

    private static PlanDepartureReminderHandler NewFailingHandler(FakeLogger<PlanDepartureReminderHandler> logger)
    {
        var store = new KnownBookingStore(new FailingWriterFactory());
        return new PlanDepartureReminderHandler(store, new FixedClock(EmailTestHost.Now), logger);
    }

    private static ReceivedEvent Confirmed(string eventId, TestBooking booking, bool isLastAttempt = false)
    {
        return TestEvents.Create(PlanDepartureReminderHandler.TriggerEventType, eventId, booking.Data(), isLastAttempt);
    }
}
