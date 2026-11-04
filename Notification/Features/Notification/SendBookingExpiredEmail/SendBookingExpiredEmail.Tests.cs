namespace Notification.Features.Notification.SendBookingExpiredEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendBookingExpiredEmailTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;

    public SendBookingExpiredEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_UnpaidSeatsReleased_SaysSeatsWereReleasedAndNothingCharged()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(Expired("E80", new TestBooking()), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal("Seats released: Mumbai to Pune on 12 Nov 2026", sent.Message.Subject);
        Assert.Contains($"We held your seats for booking {TestBooking.DefaultId}, but payment did not arrive in time", sent.Message.TextBody);
        Assert.Contains("You have not been charged.", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E80"));
        Assert.Equal("bookingExpired", entry.EmailType);
        Assert.Equal("bookingExpired", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal(TestBooking.DefaultId, entry.BookingId);
    }

    [Fact]
    public async Task HandleAsync_NoContact_LogsFailedMissingRecipient()
    {
        EmailTestHost host = NewHost();
        var booking = new TestBooking { ContactName = null, ContactEmail = null };

        await NewHandler(host).HandleAsync(Expired("E81", booking), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E81"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.False(host.RaisedWarningOrAlert());
    }

    [Fact]
    public async Task HandleAsync_NoBookingId_LogsFailedInvalidEventData()
    {
        EmailTestHost host = NewHost();
        var booking = new TestBooking { BookingId = null };

        await NewHandler(host).HandleAsync(Expired("E82", booking), CancellationToken.None);

        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E82"));
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("bookingId is missing", entry.FailureDetail);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmail()
    {
        EmailTestHost host = NewHost();
        SendBookingExpiredEmailHandler handler = NewHandler(host);
        ReceivedEvent expired = Expired("E80", new TestBooking());
        await handler.HandleAsync(expired, CancellationToken.None);

        await handler.HandleAsync(expired, CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        Assert.Single(await EntriesAsync("E80"));
    }

    [Fact]
    public async Task HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress()
    {
        await KnownBookingFixtures.SuppressAsync(_database, "ravi@gmail.com");
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Expired("E83", new TestBooking()), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E83"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("suppressedAddress", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Expired("E80", new TestBooking(), isLastAttempt: true), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E80"));
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E80", alert.Message);
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendBookingExpiredEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendBookingExpiredEmailHandler(host.Recorder);
    }

    private static ReceivedEvent Expired(string eventId, TestBooking booking, bool isLastAttempt = false)
    {
        return TestEvents.Create(SendBookingExpiredEmailHandler.TriggerEventType, eventId, booking.Data(), isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
