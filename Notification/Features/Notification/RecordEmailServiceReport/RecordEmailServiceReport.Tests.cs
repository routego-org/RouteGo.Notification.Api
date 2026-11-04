namespace Notification.Features.Notification.RecordEmailServiceReport;

[Collection(DatabaseCollection.Name)]
public sealed class RecordEmailServiceReportTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _reportedAt = new DateTimeOffset(2026, 11, 5, 4, 31, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public RecordEmailServiceReportTests(DatabaseFixture database)
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
    public async Task HandleAsync_PermanentBounce_PutsTheAddressOnTheListAndTheNextEmailToItIsSkipped()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Report(ReportKind.PermanentBounce, "ravi@gmial.com"), false, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SuppressedAddressRow row = Assert.Single(await KnownBookingFixtures.ListSuppressedAsync(_database));
        Assert.Equal("ravi@gmial.com", row.EmailAddress);
        Assert.Equal("permanentBounce", row.Reason);
        Assert.Equal("R-777", row.ProviderMessageId);
        Assert.Equal("smtp; 550 5.1.1 user unknown", row.ReportDetail);
        Assert.Equal(_reportedAt.UtcDateTime, row.ReportedAt);
        Assert.Equal(EmailTestHost.Now.UtcDateTime, row.SuppressedAt);

        var verification = new SendVerificationEmail.SendVerificationEmailHandler(host.Recorder);
        var data = new { email = "ravi@gmial.com", name = "Ravi Kumar", role = "Customer", verificationLink = "https://portal.routego.in/verify?t=1" };
        await verification.HandleAsync(TestEvents.Create("UserRegistered", "E1", data), CancellationToken.None);
        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await SendLogStore.EntriesForEventAsync(_database, "E1"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("suppressedAddress", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_Complaint_PutsTheAddressOnTheListWithReasonComplaint()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Report(ReportKind.Complaint, "sita@gmail.com"), false, CancellationToken.None);

        SuppressedAddressRow row = Assert.Single(await KnownBookingFixtures.ListSuppressedAsync(_database));
        Assert.Equal("sita@gmail.com", row.EmailAddress);
        Assert.Equal("complaint", row.Reason);
    }

    [Fact]
    public async Task HandleAsync_TemporaryBounce_ChangesNothing()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Report(ReportKind.TemporaryBounce, "full@gmail.com"), false, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Empty(await KnownBookingFixtures.ListSuppressedAsync(_database));
    }

    [Fact]
    public async Task HandleAsync_RepeatedReportInAnotherCase_LeavesOneUnchangedEntry()
    {
        EmailTestHost host = NewHost();
        RecordEmailServiceReportHandler handler = NewHandler(host);
        await handler.HandleAsync(Report(ReportKind.PermanentBounce, "ravi@gmial.com"), false, CancellationToken.None);

        await handler.HandleAsync(Report(ReportKind.Complaint, "Ravi@Gmial.com"), false, CancellationToken.None);

        SuppressedAddressRow row = Assert.Single(await KnownBookingFixtures.ListSuppressedAsync(_database));
        Assert.Equal("ravi@gmial.com", row.EmailAddress);
        Assert.Equal("permanentBounce", row.Reason);
    }

    [Fact]
    public async Task HandleAsync_ReportNamingSeveralAddresses_PutsEveryOneOnTheList()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(
            Report(ReportKind.PermanentBounce, "a@example.com", "b@example.com", "A@Example.com"), false, CancellationToken.None);

        Assert.Equal(2, (await KnownBookingFixtures.ListSuppressedAsync(_database)).Count);
    }

    [Fact]
    public async Task HandleAsync_AddressRemovedThenBouncesAgain_IsOnTheListAsANewEntry()
    {
        EmailTestHost host = NewHost();
        RecordEmailServiceReportHandler handler = NewHandler(host);
        await handler.HandleAsync(Report(ReportKind.PermanentBounce, "ravi@gmial.com"), false, CancellationToken.None);
        SuppressedAddressRow first = Assert.Single(await KnownBookingFixtures.ListSuppressedAsync(_database));
        await _database.SeedAsync("DELETE FROM notification.suppressed_addresses WHERE id = @Id;", new { first.Id });

        await handler.HandleAsync(Report(ReportKind.PermanentBounce, "ravi@gmial.com"), false, CancellationToken.None);

        SuppressedAddressRow second = Assert.Single(await KnownBookingFixtures.ListSuppressedAsync(_database));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task HandleAsync_RecordingFailsBeforeTheLastAttempt_RetriesWithoutAnAlert()
    {
        var logger = new FakeLogger<RecordEmailServiceReportHandler>();
        RecordEmailServiceReportHandler handler = NewFailingHandler(logger);

        EventHandlingOutcome outcome = await handler.HandleAsync(
            Report(ReportKind.PermanentBounce, "ravi@gmial.com"), false, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
    }

    [Fact]
    public async Task HandleAsync_RecordingFailsOnTheLastAttempt_RaisesEmailReportParkedAndReplayRecordsIt()
    {
        var logger = new FakeLogger<RecordEmailServiceReportHandler>();
        RecordEmailServiceReportHandler failing = NewFailingHandler(logger);
        EmailServiceReport report = Report(ReportKind.PermanentBounce, "ravi@gmial.com");

        EventHandlingOutcome outcome = await failing.HandleAsync(report, true, CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        FakeLogRecord alert = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.Contains("EMAIL_REPORT_PARKED", alert.Message);
        Assert.Contains("R-777", alert.Message);
        Assert.DoesNotContain("ravi@gmial.com", alert.Message);

        EmailTestHost host = NewHost();
        await NewHandler(host).HandleAsync(report, false, CancellationToken.None);
        Assert.Single(await KnownBookingFixtures.ListSuppressedAsync(_database));
    }

    private EmailTestHost NewHost()
    {
        return new EmailTestHost(_database, new FakeEmailSender());
    }

    private RecordEmailServiceReportHandler NewHandler(EmailTestHost host)
    {
        return new RecordEmailServiceReportHandler(
            SendLogStore.Writer(_database), host.Clock, new FakeLogger<RecordEmailServiceReportHandler>());
    }

    private static RecordEmailServiceReportHandler NewFailingHandler(FakeLogger<RecordEmailServiceReportHandler> logger)
    {
        return new RecordEmailServiceReportHandler(new FailingWriterFactory(), new FixedClock(EmailTestHost.Now), logger);
    }

    private static EmailServiceReport Report(ReportKind kind, params string[] addresses)
    {
        return new EmailServiceReport
        {
            Kind = kind,
            Addresses = addresses,
            ReportedAt = _reportedAt,
            ProviderMessageId = "R-777",
            Detail = "smtp; 550 5.1.1 user unknown",
        };
    }
}
