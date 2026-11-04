namespace Notification.Features.Notification.SendOperatorApprovedEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendOperatorApprovedEmailTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 2, 10, 15, 0, TimeSpan.Zero);
    private static readonly Uri _signInPage = new Uri("https://portal.routego.in/auth/login");

    private readonly DatabaseFixture _database;

    public SendOperatorApprovedEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_OperatorApproved_SendsApprovedEmailWithSignInLink()
    {
        var emails = new FakeEmailSender(EmailSendResult.Accepted("ses-333"));
        SendOperatorApprovedEmailHandler handler = CreateHandler(emails);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            OperatorApproved("E7", "owner@busco.in", "BusCo Travels"), _signInPage, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(emails.Sent);
        Assert.Equal("owner@busco.in", sent.ToAddress);
        Assert.Equal("Your RouteGo operator account is approved", sent.Message.Subject);
        Assert.Contains("Hello,", sent.Message.TextBody);
        Assert.Contains("Good news! BusCo Travels is now approved as a bus operator on RouteGo.", sent.Message.TextBody);
        Assert.Contains("Sign in: https://portal.routego.in/auth/login", sent.Message.TextBody);
        Assert.Contains("— Team RouteGo", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E7"));
        Assert.Equal("operatorApproved", entry.EmailType);
        Assert.Equal("operatorApproved", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal("ses-333", entry.ProviderMessageId);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmail()
    {
        var emails = new FakeEmailSender();
        SendOperatorApprovedEmailHandler handler = CreateHandler(emails);
        ReceivedEvent approved = OperatorApproved("E7", "owner@busco.in", "BusCo Travels");
        await handler.HandleAsync(approved, _signInPage, CancellationToken.None);

        EventHandlingOutcome outcome = await handler.HandleAsync(approved, _signInPage, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Single(emails.Sent);
        Assert.Single(await EntriesAsync("E7"));
    }

    [Fact]
    public async Task HandleAsync_NoRecipient_LogsFailedMissingRecipientWithoutSending()
    {
        var emails = new FakeEmailSender();
        var logger = new FakeLogger<EmailSendRecorder>();
        SendOperatorApprovedEmailHandler handler = CreateHandler(emails, logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            OperatorApproved("E8", null, "BusCo Travels"), _signInPage, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E8"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task HandleAsync_NoCompanyName_LogsFailedInvalidEventDataWithoutSending()
    {
        var emails = new FakeEmailSender();
        SendOperatorApprovedEmailHandler handler = CreateHandler(emails);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            OperatorApproved("E9", "owner@busco.in", null), _signInPage, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E9"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("owner@busco.in", entry.ToAddress);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        var emails = new FakeEmailSender(EmailSendResult.Unavailable("ServiceUnavailable: try later"));
        var logger = new FakeLogger<EmailSendRecorder>();
        SendOperatorApprovedEmailHandler handler = CreateHandler(emails, logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            OperatorApproved("E7", "owner@busco.in", "BusCo Travels", isLastAttempt: true),
            _signInPage, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E7"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E7", alert.Message);
    }

    private SendOperatorApprovedEmailHandler CreateHandler(
        FakeEmailSender emails, FakeLogger<EmailSendRecorder>? logger = null)
    {
        FakeLogger<EmailSendRecorder> handlerLogger = logger ?? new FakeLogger<EmailSendRecorder>();
        var ports = new EmailWorkerPorts(SendLogStore.Writer(_database), emails, new FixedClock(_now));
        return new SendOperatorApprovedEmailHandler(new EmailSendRecorder(ports, handlerLogger));
    }

    private static ReceivedEvent OperatorApproved(
        string eventId, string? email, string? companyName, bool isLastAttempt = false)
    {
        var data = new { operatorId = "op-1", companyName, email };
        return TestEvents.Create(SendOperatorApprovedEmailHandler.TriggerEventType, eventId, data, isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
