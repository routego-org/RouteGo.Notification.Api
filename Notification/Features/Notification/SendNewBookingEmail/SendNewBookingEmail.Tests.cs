namespace Notification.Features.Notification.SendNewBookingEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendNewBookingEmailTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;

    public SendNewBookingEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_BookingConfirmed_SendsOperatorThePassengersWithoutAmountOrContact()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Confirmed("E20", new TestBooking()), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("bookings@busco.in", sent.ToAddress);
        Assert.Equal($"New booking {TestBooking.DefaultId}: Mumbai to Pune on 12 Nov 2026", sent.Message.Subject);
        Assert.Contains("Hello BusCo Travels,", sent.Message.TextBody);
        Assert.Contains("Seat 12 – Ravi Kumar, 34, Male", sent.Message.TextBody);
        Assert.Contains("Seat 13 – Asha Kumar, 31, Female", sent.Message.TextBody);
        Assert.Contains("View my trips: https://portal.routego.in/operator/trips", sent.Message.TextBody);
        Assert.DoesNotContain("ravi@gmail.com", sent.Message.TextBody);
        Assert.DoesNotContain("1,300", sent.Message.TextBody);
        Assert.DoesNotContain("1300", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("newBooking", entry.EmailType);
        Assert.Equal("sent", entry.Status);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmail()
    {
        EmailTestHost host = NewHost();
        SendNewBookingEmailHandler handler = NewHandler(host);
        ReceivedEvent confirmed = Confirmed("E20", new TestBooking());
        await handler.HandleAsync(confirmed, CancellationToken.None);

        await handler.HandleAsync(confirmed, CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        Assert.Single(await EntriesAsync("E20"));
    }

    [Fact]
    public async Task HandleAsync_NoOperatorEmail_LogsFailedMissingRecipient()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Confirmed("E25", new TestBooking { OperatorEmail = null }), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E25"));
        Assert.Equal("newBooking", entry.EmailType);
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
    }

    [Fact]
    public async Task HandleAsync_NoSeats_LogsFailedInvalidEventData()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(
            Confirmed("E26", new TestBooking { Seats = new List<TestSeat>() }), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E26"));
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("seats is missing", entry.FailureDetail);
    }

    [Fact]
    public async Task HandleAsync_BookingKnownToBeCancelled_SkipsWithBookingCancelled()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Confirmed("E27", new TestBooking()), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E27"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("bookingCancelled", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Confirmed("E20", new TestBooking(), isLastAttempt: true), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("NewBooking", alert.Message);
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendNewBookingEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendNewBookingEmailHandler(host.Recorder, host.Links);
    }

    private static ReceivedEvent Confirmed(string eventId, TestBooking booking, bool isLastAttempt = false)
    {
        return TestEvents.Create(SendNewBookingEmailHandler.TriggerEventType, eventId, booking.Data(), isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
