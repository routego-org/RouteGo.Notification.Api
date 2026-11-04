namespace Notification.Features.Notification.SendOperatorRejectedEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendOperatorRejectedEmailTests : IAsyncLifetime
{
    private const int SevenDays = 7;
    private const string Reason = "GSTIN does not match the company name";

    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 2, 10, 15, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public SendOperatorRejectedEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_OperatorRejected_SendsReasonAndReapplicationWait()
    {
        var emails = new FakeEmailSender(EmailSendResult.Accepted("ses-444"));
        SendOperatorRejectedEmailHandler handler = CreateHandler(emails);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            OperatorRejected("E9", "owner@busco.in", Reason), SevenDays, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(emails.Sent);
        Assert.Equal("owner@busco.in", sent.ToAddress);
        Assert.Equal("Update on your RouteGo operator application", sent.Message.Subject);
        Assert.Contains($"Reason: {Reason}", sent.Message.TextBody);
        Assert.Contains("You can fix the issue and apply again after 7 days.", sent.Message.TextBody);
        Assert.Contains("— Team RouteGo", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E9"));
        Assert.Equal("operatorRejected", entry.EmailType);
        Assert.Equal("operatorRejected", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal("ses-444", entry.ProviderMessageId);
    }

    [Fact]
    public async Task HandleAsync_WaitOfOneDay_SaysOneDay()
    {
        var emails = new FakeEmailSender();
        SendOperatorRejectedEmailHandler handler = CreateHandler(emails);

        await handler.HandleAsync(OperatorRejected("E10", "owner@busco.in", Reason), 1, CancellationToken.None);

        SentEmail sent = Assert.Single(emails.Sent);
        Assert.Contains("You can fix the issue and apply again after 1 day.", sent.Message.TextBody);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmail()
    {
        var emails = new FakeEmailSender();
        SendOperatorRejectedEmailHandler handler = CreateHandler(emails);
        ReceivedEvent rejected = OperatorRejected("E9", "owner@busco.in", Reason);
        await handler.HandleAsync(rejected, SevenDays, CancellationToken.None);

        EventHandlingOutcome outcome = await handler.HandleAsync(rejected, SevenDays, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Single(emails.Sent);
        Assert.Single(await EntriesAsync("E9"));
    }

    [Fact]
    public async Task HandleAsync_NoRecipient_LogsFailedMissingRecipientWithoutSending()
    {
        var emails = new FakeEmailSender();
        var logger = new FakeLogger<EmailSendRecorder>();
        SendOperatorRejectedEmailHandler handler = CreateHandler(emails, logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            OperatorRejected("E11", null, Reason), SevenDays, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E11"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task HandleAsync_NoReason_LogsFailedInvalidEventDataWithoutSending()
    {
        var emails = new FakeEmailSender();
        SendOperatorRejectedEmailHandler handler = CreateHandler(emails);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            OperatorRejected("E12", "owner@busco.in", null), SevenDays, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E12"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("owner@busco.in", entry.ToAddress);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        var emails = new FakeEmailSender(EmailSendResult.Unavailable("ServiceUnavailable: try later"));
        var logger = new FakeLogger<EmailSendRecorder>();
        SendOperatorRejectedEmailHandler handler = CreateHandler(emails, logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            OperatorRejected("E9", "owner@busco.in", Reason, isLastAttempt: true), SevenDays, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E9"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E9", alert.Message);
    }

    private SendOperatorRejectedEmailHandler CreateHandler(
        FakeEmailSender emails, FakeLogger<EmailSendRecorder>? logger = null)
    {
        FakeLogger<EmailSendRecorder> handlerLogger = logger ?? new FakeLogger<EmailSendRecorder>();
        var ports = new EmailWorkerPorts(SendLogStore.Writer(_database), emails, new FixedClock(_now));
        return new SendOperatorRejectedEmailHandler(new EmailSendRecorder(ports, handlerLogger));
    }

    private static ReceivedEvent OperatorRejected(
        string eventId, string? email, string? reason, bool isLastAttempt = false)
    {
        var data = new { operatorId = "op-1", email, reason };
        return TestEvents.Create(SendOperatorRejectedEmailHandler.TriggerEventType, eventId, data, isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
