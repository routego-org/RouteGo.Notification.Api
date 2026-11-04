namespace Notification.Features.Notification.SendTicketReadyEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendTicketReadyEmailTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _firstChange = new DateTimeOffset(2026, 11, 5, 4, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _secondChange = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public SendTicketReadyEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_FirstIssue_SendsTicketReadyWithTicketLink()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Ticket("E30", "FirstIssue", _firstChange), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal("Your RouteGo e-ticket: Mumbai to Pune on 12 Nov 2026", sent.Message.Subject);
        Assert.Contains($"Your e-ticket for booking {TestBooking.DefaultId} is ready.", sent.Message.TextBody);
        Assert.Contains($"Open my ticket: https://portal.routego.in/tickets/{TestBooking.DefaultId}", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E30"));
        Assert.Equal("ticketReady", entry.EmailType);
        Assert.Equal("ticketReady", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal(TestBooking.DefaultId, entry.BookingId);
        Assert.Equal<DateTime?>(TestBooking.DefaultDeparture.UtcDateTime, entry.DepartsAt);
    }

    [Fact]
    public async Task HandleAsync_Reissue_SendsUpdatedTicketThatReplacesTheEarlierOne()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Ticket("E31", "Reissue", _secondChange), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("Your updated RouteGo e-ticket: Mumbai to Pune on 12 Nov 2026", sent.Message.Subject);
        Assert.Contains("It replaces the ticket we sent you before", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E31"));
        Assert.Equal("ticketReissued", entry.EmailType);
    }

    [Fact]
    public async Task HandleAsync_SameTicketUnderNewEventId_SendsOneEmail()
    {
        EmailTestHost host = NewHost();
        SendTicketReadyEmailHandler handler = NewHandler(host);
        await handler.HandleAsync(Ticket("E30", "FirstIssue", _firstChange), CancellationToken.None);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            Ticket("E30-again", "FirstIssue", _firstChange), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Single(host.Emails.Sent);
        Assert.Empty(await EntriesAsync("E30-again"));
    }

    [Fact]
    public async Task HandleAsync_SecondSeatChange_SendsSecondUpdatedTicket()
    {
        EmailTestHost host = NewHost();
        SendTicketReadyEmailHandler handler = NewHandler(host);
        await handler.HandleAsync(Ticket("E31", "Reissue", _secondChange), CancellationToken.None);

        var thirdChange = new DateTimeOffset(2026, 11, 8, 11, 0, 0, TimeSpan.Zero);
        await handler.HandleAsync(Ticket("E32", "Reissue", thirdChange), CancellationToken.None);

        Assert.Equal(2, host.Emails.Sent.Count);
    }

    [Fact]
    public async Task HandleAsync_NoContactEmail_LogsFailedMissingRecipient()
    {
        EmailTestHost host = NewHost();
        var booking = new TestBooking { ContactEmail = null };

        await NewHandler(host).HandleAsync(Ticket("E33", "FirstIssue", _firstChange, booking), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E33"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.False(host.RaisedWarningOrAlert());
    }

    [Fact]
    public async Task HandleAsync_UnknownIssueKind_LogsFailedInvalidEventDataAsTicketReady()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Ticket("E34", "Draft", _firstChange), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E34"));
        Assert.Equal("ticketReady", entry.EmailType);
        Assert.Equal("failed", entry.Status);
        Assert.Equal("invalidEventData", entry.FailureReason);
    }

    [Fact]
    public async Task HandleAsync_NoBookingId_LogsFailedInvalidEventData()
    {
        EmailTestHost host = NewHost();
        var booking = new TestBooking { BookingId = null };

        await NewHandler(host).HandleAsync(Ticket("E35", "FirstIssue", _firstChange, booking), CancellationToken.None);

        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E35"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("bookingId is missing", entry.FailureDetail);
    }

    [Fact]
    public async Task HandleAsync_BookingKnownToBeCancelled_SkipsWithBookingCancelled()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Ticket("E36", "FirstIssue", _firstChange), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E36"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("bookingCancelled", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress()
    {
        await KnownBookingFixtures.SuppressAsync(_database, "ravi@gmail.com");
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Ticket("E37", "FirstIssue", _firstChange), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E37"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("suppressedAddress", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Ticket("E30", "FirstIssue", _firstChange, isLastAttempt: true), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E30"));
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E30", alert.Message);
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendTicketReadyEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendTicketReadyEmailHandler(host.Recorder, host.Links);
    }

    private static ReceivedEvent Ticket(
        string eventId, string issueKind, DateTimeOffset changedAt, TestBooking? booking = null, bool isLastAttempt = false)
    {
        TestBooking ticketBooking = booking ?? new TestBooking();
        var extras = new Dictionary<string, object?> { ["issueKind"] = issueKind, ["ticketChangeAt"] = changedAt };
        return TestEvents.Create(SendTicketReadyEmailHandler.TriggerEventType, eventId, ticketBooking.Data(extras), isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
