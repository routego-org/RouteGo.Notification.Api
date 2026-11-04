namespace Notification.Features.Notification.SendVerificationEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendVerificationEmailTests : IAsyncLifetime
{
    private const string Link = "https://portal.routego.in/verify?token=abc123";
    private const string NewLink = "https://portal.routego.in/verify?token=def456";

    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 2, 10, 15, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public SendVerificationEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_CustomerRegistered_SendsCustomerWordingAndLogsSent()
    {
        var emails = new FakeEmailSender(EmailSendResult.Accepted("ses-111"));
        SendVerificationEmailHandler handler = CreateHandler(emails);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            UserRegistered("E1", "ravi@gmail.com", "Ravi Kumar", "Customer", Link), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal("Verify your email for RouteGo", sent.Message.Subject);
        Assert.Contains("Hi Ravi Kumar,", sent.Message.TextBody);
        Assert.Contains($"Verify my email: {Link}", sent.Message.TextBody);
        Assert.Contains("— Team RouteGo", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E1"));
        Assert.Equal("customerVerification", entry.EmailType);
        Assert.Equal("userRegistered", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal("ses-111", entry.ProviderMessageId);
        Assert.Equal("corr-E1", entry.CorrelationId);
        Assert.Equal(sent.Message.HtmlBody, entry.BodyHtml);
    }

    [Fact]
    public async Task HandleAsync_OperatorRegistered_SendsOperatorWording()
    {
        var emails = new FakeEmailSender();
        SendVerificationEmailHandler handler = CreateHandler(emails);

        await handler.HandleAsync(
            UserRegistered("E2", "owner@busco.in", "Anil Shah", "Operator", Link), CancellationToken.None);

        SentEmail sent = Assert.Single(emails.Sent);
        Assert.Equal("Verify your email for your RouteGo operator application", sent.Message.Subject);
        Assert.Contains("After you verify, our team will review your application.", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E2"));
        Assert.Equal("operatorVerification", entry.EmailType);
        Assert.Equal("sent", entry.Status);
    }

    [Fact]
    public async Task HandleAsync_RoleInLowerCase_SendsCustomerWording()
    {
        var emails = new FakeEmailSender();
        SendVerificationEmailHandler handler = CreateHandler(emails);

        await handler.HandleAsync(
            UserRegistered("E3", "ravi@gmail.com", "Ravi Kumar", "customer", Link), CancellationToken.None);

        SentEmail sent = Assert.Single(emails.Sent);
        Assert.Equal("Verify your email for RouteGo", sent.Message.Subject);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmail()
    {
        var emails = new FakeEmailSender();
        SendVerificationEmailHandler handler = CreateHandler(emails);
        ReceivedEvent registered = UserRegistered("E1", "ravi@gmail.com", "Ravi Kumar", "Customer", Link);
        await handler.HandleAsync(registered, CancellationToken.None);

        EventHandlingOutcome outcome = await handler.HandleAsync(registered, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Single(emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E1"));
        Assert.Equal("customerVerification", entry.EmailType);
    }

    [Fact]
    public async Task HandleAsync_NewEventForSameAddress_SendsSecondEmailWithNewLink()
    {
        var emails = new FakeEmailSender();
        SendVerificationEmailHandler handler = CreateHandler(emails);
        await handler.HandleAsync(
            UserRegistered("E1", "ravi@gmail.com", "Ravi Kumar", "Customer", Link), CancellationToken.None);

        await handler.HandleAsync(
            UserRegistered("E2", "ravi@gmail.com", "Ravi Kumar", "Customer", NewLink), CancellationToken.None);

        Assert.Equal(2, emails.Sent.Count);
        Assert.Contains(NewLink, emails.Sent[1].Message.TextBody);
        Assert.Single(await EntriesAsync("E1"));
        Assert.Single(await EntriesAsync("E2"));
    }

    [Fact]
    public async Task HandleAsync_NoRecipient_LogsFailedMissingRecipientWithoutSending()
    {
        var emails = new FakeEmailSender();
        var logger = new FakeLogger<EmailSendRecorder>();
        SendVerificationEmailHandler handler = CreateHandler(emails, logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            UserRegistered("E4", null, "Ravi Kumar", "Customer", Link), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E4"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.Null(entry.ToAddress);
        Assert.Null(entry.Subject);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task HandleAsync_AdminRole_LogsFailedInvalidEventDataWithoutSending()
    {
        var emails = new FakeEmailSender();
        var logger = new FakeLogger<EmailSendRecorder>();
        SendVerificationEmailHandler handler = CreateHandler(emails, logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            UserRegistered("E5", "boss@routego.in", "Boss", "Admin", Link), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E5"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("customerVerification", entry.EmailType);
        Assert.Equal("boss@routego.in", entry.ToAddress);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task HandleAsync_EmailServiceRefusesAddress_LogsFailedRefusedWithReason()
    {
        var emails = new FakeEmailSender(EmailSendResult.Refused("Email address is not verified."));
        var logger = new FakeLogger<EmailSendRecorder>();
        SendVerificationEmailHandler handler = CreateHandler(emails, logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            UserRegistered("E6", "nobody@example.com", "No Body", "Customer", Link), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E6"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("refusedByEmailService", entry.FailureReason);
        Assert.Equal("Email address is not verified.", entry.FailureDetail);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task HandleAsync_TemporaryFailureThenRecovery_SendsOnceOnSameEntry()
    {
        var emails = new FakeEmailSender(EmailSendResult.Unavailable("Throttling: Maximum sending rate exceeded."));
        SendVerificationEmailHandler handler = CreateHandler(emails);
        ReceivedEvent registered = UserRegistered("E1", "ravi@gmail.com", "Ravi Kumar", "Customer", Link);

        EventHandlingOutcome firstOutcome = await handler.HandleAsync(registered, CancellationToken.None);
        SendLogEntryRow afterFailure = Assert.Single(await EntriesAsync("E1"));
        EventHandlingOutcome secondOutcome = await handler.HandleAsync(registered, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, firstOutcome);
        Assert.Equal("sending", afterFailure.Status);
        Assert.Equal("Throttling: Maximum sending rate exceeded.", afterFailure.FailureDetail);
        Assert.Equal(EventHandlingOutcome.Consumed, secondOutcome);
        SendLogEntryRow afterRecovery = Assert.Single(await EntriesAsync("E1"));
        Assert.Equal(afterFailure.Id, afterRecovery.Id);
        Assert.Equal("sent", afterRecovery.Status);
        Assert.Equal(2, emails.Sent.Count);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked()
    {
        var emails = new FakeEmailSender(EmailSendResult.Unavailable("ServiceUnavailable: try later"));
        var logger = new FakeLogger<EmailSendRecorder>();
        SendVerificationEmailHandler handler = CreateHandler(emails, logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            UserRegistered("E1", "ravi@gmail.com", "Ravi Kumar", "Customer", Link, isLastAttempt: true),
            CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E1"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E1", alert.Message);
        Assert.Contains("UserRegistered", alert.Message);
        Assert.Contains("CustomerVerification", alert.Message);
    }

    [Fact]
    public async Task HandleAsync_ReplayOfParkedEmail_SendsOnSameEntry()
    {
        SendVerificationEmailHandler parking = CreateHandler(
            new FakeEmailSender(EmailSendResult.Unavailable("ServiceUnavailable: try later")));
        await parking.HandleAsync(
            UserRegistered("E1", "ravi@gmail.com", "Ravi Kumar", "Customer", Link, isLastAttempt: true),
            CancellationToken.None);
        SendLogEntryRow parked = Assert.Single(await EntriesAsync("E1"));
        var recovered = new FakeEmailSender(EmailSendResult.Accepted("ses-222"));
        SendVerificationEmailHandler handler = CreateHandler(recovered);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            UserRegistered("E1", "ravi@gmail.com", "Ravi Kumar", "Customer", Link), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Single(recovered.Sent);
        SendLogEntryRow replayed = Assert.Single(await EntriesAsync("E1"));
        Assert.Equal(parked.Id, replayed.Id);
        Assert.Equal("sent", replayed.Status);
        Assert.Null(replayed.FailureReason);
        Assert.Equal("ses-222", replayed.ProviderMessageId);
    }

    [Fact]
    public async Task HandleAsync_EventsWaitingAfterRestart_SendsEveryOne()
    {
        var emails = new FakeEmailSender();
        SendVerificationEmailHandler handler = CreateHandler(emails);

        await handler.HandleAsync(UserRegistered("E7", "a@gmail.com", "A", "Customer", Link), CancellationToken.None);
        await handler.HandleAsync(UserRegistered("E8", "b@gmail.com", "B", "Customer", Link), CancellationToken.None);
        await handler.HandleAsync(UserRegistered("E9", "c@gmail.com", "C", "Customer", Link), CancellationToken.None);

        Assert.Equal(3, emails.Sent.Count);
        Assert.Equal("sent", Assert.Single(await EntriesAsync("E7")).Status);
        Assert.Equal("sent", Assert.Single(await EntriesAsync("E8")).Status);
        Assert.Equal("sent", Assert.Single(await EntriesAsync("E9")).Status);
    }

    private SendVerificationEmailHandler CreateHandler(
        FakeEmailSender emails, FakeLogger<EmailSendRecorder>? logger = null)
    {
        FakeLogger<EmailSendRecorder> handlerLogger = logger ?? new FakeLogger<EmailSendRecorder>();
        var ports = new EmailWorkerPorts(SendLogStore.Writer(_database), emails, new FixedClock(_now));
        return new SendVerificationEmailHandler(new EmailSendRecorder(ports, handlerLogger));
    }

    private static ReceivedEvent UserRegistered(
        string eventId, string? email, string fullName, string role, string link, bool isLastAttempt = false)
    {
        var data = new { userId = "u-1", email, name = fullName, role, verificationLink = link };
        return TestEvents.Create(SendVerificationEmailHandler.TriggerEventType, eventId, data, isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
