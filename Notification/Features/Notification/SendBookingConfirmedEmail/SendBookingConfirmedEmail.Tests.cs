namespace Notification.Features.Notification.SendBookingConfirmedEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendBookingConfirmedEmailTests : IAsyncLifetime
{
    private const string Ravi = "ravi@gmail.com";

    private readonly DatabaseFixture _database;

    public SendBookingConfirmedEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_BookingConfirmed_SendsConfirmationWithSeatsTotalAndTicketLink()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(Confirmed("E20", new TestBooking()), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal(Ravi, sent.ToAddress);
        Assert.Equal("Booking confirmed: Mumbai to Pune on 12 Nov 2026", sent.Message.Subject);
        Assert.Contains($"Booking ID: {TestBooking.DefaultId}", sent.Message.TextBody);
        Assert.Contains("Departs: 12 Nov 2026, 20:00 IST", sent.Message.TextBody);
        Assert.Contains("Bus: BusCo Volvo (Sleeper, AC)", sent.Message.TextBody);
        Assert.Contains("Seat 12 – Ravi Kumar", sent.Message.TextBody);
        Assert.Contains("Seat 13 – Asha Kumar", sent.Message.TextBody);
        Assert.Contains("Total paid: ₹1,300", sent.Message.TextBody);
        Assert.Contains($"View my ticket: https://portal.routego.in/tickets/{TestBooking.DefaultId}", sent.Message.TextBody);
        Assert.Contains("— Team RouteGo", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("bookingConfirmed", entry.EmailType);
        Assert.Equal("bookingConfirmed", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal(TestBooking.DefaultId, entry.BookingId);
        Assert.Equal<DateTime?>(TestBooking.DefaultDeparture.UtcDateTime, entry.DepartsAt);
        Assert.Equal("E20", entry.RepeatKey);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmail()
    {
        EmailTestHost host = NewHost();
        SendBookingConfirmedEmailHandler handler = NewHandler(host);
        ReceivedEvent confirmed = Confirmed("E20", new TestBooking());
        await handler.HandleAsync(confirmed, CancellationToken.None);

        EventHandlingOutcome outcome = await handler.HandleAsync(confirmed, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Single(host.Emails.Sent);
        Assert.Single(await EntriesAsync("E20"));
    }

    [Fact]
    public async Task HandleAsync_NoContactEmail_LogsFailedMissingRecipientWithoutAlert()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Confirmed("E21", new TestBooking { ContactEmail = null }), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E21"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.False(host.RaisedWarningOrAlert());
    }

    [Fact]
    public async Task HandleAsync_NoSeats_LogsFailedInvalidEventDataNamingTheSeats()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(
            Confirmed("E22", new TestBooking { Seats = new List<TestSeat>() }), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E22"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("seats is missing", entry.FailureDetail);
    }

    [Fact]
    public async Task HandleAsync_BookingKnownToBeCancelled_SkipsWithBookingCancelled()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Confirmed("E23", new TestBooking()), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E23"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("bookingCancelled", entry.SkipReason);
        Assert.False(host.RaisedWarningOrAlert());
    }

    [Fact]
    public async Task HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddressButKeepsTheEmail()
    {
        await KnownBookingFixtures.SuppressAsync(_database, "Ravi@Gmail.com");
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Confirmed("E24", new TestBooking()), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E24"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("suppressedAddress", entry.SkipReason);
        Assert.Equal("Booking confirmed: Mumbai to Pune on 12 Nov 2026", entry.Subject);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Confirmed("E20", new TestBooking(), isLastAttempt: true), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E20"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E20", alert.Message);
        Assert.Contains("BookingConfirmed", alert.Message);
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendBookingConfirmedEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendBookingConfirmedEmailHandler(host.Recorder, host.Links);
    }

    private static ReceivedEvent Confirmed(string eventId, TestBooking booking, bool isLastAttempt = false)
    {
        return TestEvents.Create(SendBookingConfirmedEmailHandler.TriggerEventType, eventId, booking.Data(), isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
