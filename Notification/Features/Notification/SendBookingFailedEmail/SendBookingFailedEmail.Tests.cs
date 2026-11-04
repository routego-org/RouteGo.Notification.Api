namespace Notification.Features.Notification.SendBookingFailedEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendBookingFailedEmailTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;

    public SendBookingFailedEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_TripCancelled_SaysNothingWasCharged()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Failed("E70", "TripCancelled", new TestBooking()), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal("Booking not completed: Mumbai to Pune on 12 Nov 2026", sent.Message.Subject);
        Assert.Contains("The bus operator has cancelled the trip", sent.Message.TextBody);
        Assert.Contains($"your booking {TestBooking.DefaultId} was not completed", sent.Message.TextBody);
        Assert.Contains("You have not been charged.", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E70"));
        Assert.Equal("bookingFailed", entry.EmailType);
        Assert.Equal("bookingFailed", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal(TestBooking.DefaultId, entry.BookingId);
        Assert.Equal<DateTime?>(TestBooking.DefaultDeparture.UtcDateTime, entry.DepartsAt);
    }

    [Fact]
    public async Task HandleAsync_SeatsLost_SaysThePaymentIsBeingReturned()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Failed("E72", "SeatsLost", new TestBooking()), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Contains("your seats could no longer be confirmed", sent.Message.TextBody);
        Assert.Contains("We are returning your full payment and will email you when it has been returned.", sent.Message.TextBody);
        Assert.DoesNotContain("You have not been charged.", sent.Message.TextBody);
    }

    [Theory]
    [InlineData("SeatsUnavailable")]
    [InlineData("InventoryUnavailable")]
    [InlineData("PaymentUnavailable")]
    [InlineData("PaymentDeclined")]
    [InlineData("Abandoned")]
    [InlineData("SomethingNew")]
    public async Task HandleAsync_ReasonTheCustomerSawOnScreen_SendsNothingAndRecordsNothing(string reason)
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Failed("E73", reason, new TestBooking()), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(host.Emails.Sent);
        Assert.Empty(await EntriesAsync("E73"));
        Assert.False(host.RaisedWarningOrAlert());
    }

    [Fact]
    public async Task HandleAsync_NoContact_LogsFailedMissingRecipient()
    {
        EmailTestHost host = NewHost();
        var booking = new TestBooking { ContactName = null, ContactEmail = null };

        await NewHandler(host).HandleAsync(Failed("E74", "TripCancelled", booking), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E74"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.False(host.RaisedWarningOrAlert());
    }

    [Fact]
    public async Task HandleAsync_ContactButNoTrip_LogsFailedInvalidEventData()
    {
        EmailTestHost host = NewHost();
        var booking = new TestBooking { Source = null, Destination = null, DepartureUtc = null };

        await NewHandler(host).HandleAsync(Failed("E75", "TripCancelled", booking), CancellationToken.None);

        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E75"));
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("trip source is missing", entry.FailureDetail);
    }

    [Fact]
    public async Task HandleAsync_NoContactName_GreetsHello()
    {
        EmailTestHost host = NewHost();
        var booking = new TestBooking { ContactName = null };

        await NewHandler(host).HandleAsync(Failed("E76", "TripCancelled", booking), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.StartsWith("Hello,", sent.Message.TextBody);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmail()
    {
        EmailTestHost host = NewHost();
        SendBookingFailedEmailHandler handler = NewHandler(host);
        ReceivedEvent failed = Failed("E70", "TripCancelled", new TestBooking());
        await handler.HandleAsync(failed, CancellationToken.None);

        await handler.HandleAsync(failed, CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        Assert.Single(await EntriesAsync("E70"));
    }

    [Fact]
    public async Task HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress()
    {
        await KnownBookingFixtures.SuppressAsync(_database, "ravi@gmail.com");
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Failed("E77", "TripCancelled", new TestBooking()), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E77"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("suppressedAddress", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Failed("E71", "TripCancelled", new TestBooking(), isLastAttempt: true), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E71"));
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E71", alert.Message);
    }

    [Fact]
    public async Task HandleAsync_TripCancelled_DoesNotMakeTheBookingKnown()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Failed("E70", "TripCancelled", new TestBooking()), CancellationToken.None);

        Assert.Null(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendBookingFailedEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendBookingFailedEmailHandler(host.Recorder);
    }

    private static ReceivedEvent Failed(string eventId, string reason, TestBooking booking, bool isLastAttempt = false)
    {
        var extras = new Dictionary<string, object?> { ["reason"] = reason };
        return TestEvents.Create(SendBookingFailedEmailHandler.TriggerEventType, eventId, booking.Data(extras), isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
