namespace Notification.Features.Notification.SendRefundCompletedEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendRefundCompletedEmailTests : IAsyncLifetime
{
    private static readonly Guid _lateBookingId = Guid.Parse("7d1f0a52-3c4b-4e8a-9f2d-1b6c5e8a9d01");

    private readonly DatabaseFixture _database;

    public SendRefundCompletedEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_KnownBooking_SendsToTheEventEmailGreetingTheKnownName()
    {
        await KnownBookingFixtures.InsertAsync(_database, new StoredKnownBooking());
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Refund("E60", TestBooking.DefaultId, 650, "ravi@gmail.com"), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal("Your RouteGo refund of ₹650 is complete", sent.Message.Subject);
        Assert.Contains("Hi Ravi Kumar,", sent.Message.TextBody);
        Assert.Contains($"We have returned ₹650 for your booking {TestBooking.DefaultId}", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E60"));
        Assert.Equal("refundCompleted", entry.EmailType);
        Assert.Equal("refundCompleted", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal(TestBooking.DefaultId, entry.BookingId);
        Assert.Equal(TestBooking.DefaultId.ToString("D"), entry.RepeatKey);
    }

    [Fact]
    public async Task HandleAsync_BookingNeverKnown_StillEmailsTheCustomerGreetingHello()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Refund("E61", _lateBookingId, 900, "priya@example.com"), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("priya@example.com", sent.ToAddress);
        Assert.Contains("Hello,", sent.Message.TextBody);
        Assert.Contains($"for your booking {_lateBookingId}", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E61"));
        Assert.Equal("sent", entry.Status);
    }

    [Fact]
    public async Task HandleAsync_NoEmailInEventButBookingKnown_SendsToTheKnownContact()
    {
        await KnownBookingFixtures.InsertAsync(_database, new StoredKnownBooking());
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Refund("E62", TestBooking.DefaultId, 650, null), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
    }

    [Fact]
    public async Task HandleAsync_NoEmailAndBookingNotKnown_LogsFailedMissingRecipientWithoutAlert()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Refund("E63", _lateBookingId, 900, null), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E63"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.False(host.RaisedWarningOrAlert());
    }

    [Fact]
    public async Task HandleAsync_SameBookingUnderNewEventId_SendsOneEmail()
    {
        EmailTestHost host = NewHost();
        SendRefundCompletedEmailHandler handler = NewHandler(host);
        await handler.HandleAsync(Refund("E60", TestBooking.DefaultId, 650, "ravi@gmail.com"), CancellationToken.None);

        await handler.HandleAsync(Refund("E60-again", TestBooking.DefaultId, 650, "ravi@gmail.com"), CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        Assert.Empty(await EntriesAsync("E60-again"));
    }

    [Fact]
    public async Task HandleAsync_NoBookingId_LogsFailedInvalidEventData()
    {
        EmailTestHost host = NewHost();
        var data = new Dictionary<string, object?> { ["amount"] = 650, ["customerEmail"] = "ravi@gmail.com" };

        await NewHandler(host).HandleAsync(
            TestEvents.Create(SendRefundCompletedEmailHandler.TriggerEventType, "E64", data), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E64"));
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("bookingId is missing", entry.FailureDetail);
    }

    [Fact]
    public async Task HandleAsync_NoAmount_LogsFailedInvalidEventData()
    {
        EmailTestHost host = NewHost();
        var data = new Dictionary<string, object?> { ["bookingId"] = TestBooking.DefaultId, ["customerEmail"] = "ravi@gmail.com" };

        await NewHandler(host).HandleAsync(
            TestEvents.Create(SendRefundCompletedEmailHandler.TriggerEventType, "E65", data), CancellationToken.None);

        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E65"));
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("amount is missing", entry.FailureDetail);
    }

    [Fact]
    public async Task HandleAsync_CancelledBooking_StillSendsButNotToADoNotSendAddress()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        await KnownBookingFixtures.SuppressAsync(_database, "ravi@gmial.com");
        EmailTestHost host = NewHost();
        SendRefundCompletedEmailHandler handler = NewHandler(host);

        await handler.HandleAsync(Refund("E66", TestBooking.DefaultId, 650, "ravi@gmial.com"), CancellationToken.None);
        await handler.HandleAsync(Refund("E67", _lateBookingId, 650, "ravi@gmail.com"), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        SendLogEntryRow skipped = Assert.Single(await EntriesAsync("E66"));
        Assert.Equal("skipped", skipped.Status);
        Assert.Equal("suppressedAddress", skipped.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Refund("E60", TestBooking.DefaultId, 650, "ravi@gmail.com", isLastAttempt: true), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E60"));
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E60", alert.Message);
    }

    [Fact]
    public async Task HandleAsync_CustomerEmail_NeverReachesALogLine()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Refund("E68", _lateBookingId, 900, "priya@example.com"), CancellationToken.None);

        Assert.DoesNotContain(host.Log.Collector.GetSnapshot(), record => record.Message.Contains("priya@example.com"));
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendRefundCompletedEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendRefundCompletedEmailHandler(host.Recorder, host.KnownBookings);
    }

    private static ReceivedEvent Refund(
        string eventId, Guid bookingId, int amount, string? customerEmail, bool isLastAttempt = false)
    {
        var data = new Dictionary<string, object?>
        {
            ["bookingId"] = bookingId,
            ["amount"] = amount,
            ["currency"] = "INR",
            ["customerEmail"] = customerEmail,
        };
        return TestEvents.Create(SendRefundCompletedEmailHandler.TriggerEventType, eventId, data, isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
