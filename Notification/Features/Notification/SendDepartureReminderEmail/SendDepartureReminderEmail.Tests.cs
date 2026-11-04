namespace Notification.Features.Notification.SendDepartureReminderEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendDepartureReminderEmailTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _dueAt = TestBooking.DefaultDeparture.AddHours(-24);

    private readonly DatabaseFixture _database;

    public SendDepartureReminderEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_ReminderDue_SendsTheCurrentSeatsAndMarksTheReminderDone()
    {
        await SeedDueReminderAsync();
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(TestBooking.DefaultId, false, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal("Reminder: your bus to Pune leaves tomorrow", sent.Message.Subject);
        Assert.Contains("Departs: 12 Nov 2026, 20:00 IST", sent.Message.TextBody);
        Assert.Contains("Bus: BusCo Volvo", sent.Message.TextBody);
        Assert.Contains("Seats: 13, 14", sent.Message.TextBody);
        Assert.Contains($"Open my ticket: https://portal.routego.in/tickets/{TestBooking.DefaultId}", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("departureReminder", entry.EmailType);
        Assert.Equal("bookingConfirmed", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal(TestBooking.DefaultId, entry.BookingId);
        Assert.Equal(TestBooking.DefaultId.ToString("D"), entry.RepeatKey);
        Assert.Equal("corr-E20", entry.CorrelationId);
        await AssertReminderStateAsync("done");
    }

    [Fact]
    public async Task HandleAsync_BookingCancelledAndReminderWithdrawn_SkipsIfItStillFires()
    {
        StoredKnownBooking withdrawn = StoredKnownBooking.WithReminderDue(TestBooking.DefaultId, TestBooking.DefaultDeparture, _dueAt) with
        {
            Status = "cancelled",
            ReminderState = "withdrawn",
            CancelledAt = EmailTestHost.Now,
        };
        await KnownBookingFixtures.InsertAsync(_database, withdrawn);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(TestBooking.DefaultId, false, CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("bookingCancelled", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_ReminderHandledAfterDeparture_SkipsWithDeparturePassed()
    {
        DateTimeOffset departed = EmailTestHost.Now.AddDays(-1);
        await KnownBookingFixtures.InsertAsync(
            _database, StoredKnownBooking.WithReminderDue(TestBooking.DefaultId, departed, departed.AddHours(-24)));
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(TestBooking.DefaultId, false, CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("departurePassed", entry.SkipReason);
        await AssertReminderStateAsync("done");
    }

    [Fact]
    public async Task HandleAsync_ReminderFiresTwice_SendsOneEmail()
    {
        await SeedDueReminderAsync();
        EmailTestHost host = NewHost();
        SendDepartureReminderEmailHandler handler = NewHandler(host);
        await handler.HandleAsync(TestBooking.DefaultId, false, CancellationToken.None);

        await handler.HandleAsync(TestBooking.DefaultId, false, CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        Assert.Single(await EntriesAsync("E20"));
    }

    [Fact]
    public async Task HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress()
    {
        await SeedDueReminderAsync();
        await KnownBookingFixtures.SuppressAsync(_database, "ravi@gmail.com");
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(TestBooking.DefaultId, false, CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("suppressedAddress", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_TemporaryFailureBeforeTheLastAttempt_LeavesTheReminderPlanned()
    {
        await SeedDueReminderAsync();
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(TestBooking.DefaultId, false, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        await AssertReminderStateAsync("planned");
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParkedNamingTheConfirmedEvent()
    {
        await SeedDueReminderAsync();
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(TestBooking.DefaultId, true, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E20", alert.Message);
        Assert.Contains("DepartureReminder", alert.Message);
        await AssertReminderStateAsync("done");
    }

    [Fact]
    public async Task HandleAsync_BookingNotKnown_SendsNothing()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(TestBooking.DefaultId, false, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(host.Emails.Sent);
    }

    private Task SeedDueReminderAsync()
    {
        var booking = StoredKnownBooking.WithReminderDue(TestBooking.DefaultId, TestBooking.DefaultDeparture, _dueAt);
        return KnownBookingFixtures.InsertAsync(_database, booking);
    }

    private async Task AssertReminderStateAsync(string expected)
    {
        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal(expected, known.ReminderState);
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendDepartureReminderEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendDepartureReminderEmailHandler(host.Recorder, host.KnownBookings, host.Links);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
