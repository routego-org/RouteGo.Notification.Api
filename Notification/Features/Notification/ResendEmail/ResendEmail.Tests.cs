namespace Notification.Features.Notification.ResendEmail;

[Collection(DatabaseCollection.Name)]
public sealed class ResendEmailTests : IAsyncLifetime
{
    private const string VerificationText = "Hi Ravi Kumar,\n\nVerify my email: https://portal.routego.in/verify?token=abc123";

    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;
    private readonly FixedClock _clock = new FixedClock(_now);

    public ResendEmailTests(DatabaseFixture database)
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

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task ResendEmail_InternalKeyMissingOrWrong_ReturnsUnauthorizedAndSendsNothing(string? internalKey)
    {
        Guid originalId = await SendLogStore.InsertAsync(_database, OperatorApproved("E105"));
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);
        HttpClient client = InternalApiHost.CreateClient(host, internalKey);

        HttpResponseMessage response = await client.PostAsync(ResendPath(originalId), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("UNAUTHORIZED", problem.Code);
        Assert.Empty(emails.Sent);
        Assert.Single(await SendLogStore.EntriesForEventAsync(_database, "E105"));
    }

    [Fact]
    public async Task ResendEmail_FailedEntry_SendsSameEmailToSameRecipient()
    {
        StoredSendLogEntry original = FailedOperatorApproved("E105");
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(originalId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SentEmail sent = Assert.Single(emails.Sent);
        Assert.Equal("owner@busco.in", sent.ToAddress);
        Assert.Equal(new EmailMessage(original.Subject!, original.BodyHtml!, original.BodyText!), sent.Message);
    }

    [Fact]
    public async Task ResendEmail_FailedEntry_ReturnsNewSentEntryAndLeavesOriginalUnchanged()
    {
        Guid originalId = await SendLogStore.InsertAsync(_database, FailedOperatorApproved("E105"));
        var emails = new FakeEmailSender(EmailSendResult.Accepted("ses-resend-1"));
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(originalId), null);

        ResendEmailResponse newEntry = await HttpJson.ReadAsync<ResendEmailResponse>(response);
        Assert.Equal(SendStatus.Sent, newEntry.Status);
        Assert.Equal(originalId, newEntry.ResendOf);
        Assert.Equal("E105", newEntry.EventId);
        Assert.Equal(EmailType.OperatorApproved, newEntry.EmailType);
        Assert.Equal("ses-resend-1", newEntry.ProviderMessageId);
        Assert.Equal(_now, newEntry.SentAt);
        SendLogEntryRow? stored = await SendLogStore.EntryAsync(_database, newEntry.SendLogEntryId);
        Assert.NotNull(stored);
        Assert.Equal("sent", stored.Status);
        Assert.Equal("corr-E105", stored.CorrelationId);
        SendLogEntryRow? unchanged = await SendLogStore.EntryAsync(_database, originalId);
        Assert.NotNull(unchanged);
        Assert.Equal("failed", unchanged.Status);
        Assert.Equal("emailServiceUnavailable", unchanged.FailureReason);
    }

    [Fact]
    public async Task ResendEmail_SentEntry_SendsAgainAndRecordsNewSentEntry()
    {
        StoredSendLogEntry original = OperatorApproved("E101") with
        {
            EventType = "operatorRejected",
            EmailType = "operatorRejected",
            Subject = "Your RouteGo operator application",
        };
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(originalId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(emails.Sent);
        IReadOnlyList<SendLogEntryRow> entries = await SendLogStore.EntriesForEventAsync(_database, "E101");
        Assert.Equal(2, entries.Count);
        Assert.Equal("sent", entries[1].Status);
        Assert.Equal(originalId, entries[1].ResendOf);
    }

    [Fact]
    public async Task ResendEmail_VerificationEmailInsideLinkLifetime_SendsSameLink()
    {
        var original = new StoredSendLogEntry
        {
            EventId = "E120",
            BodyText = VerificationText,
            EventOccurredAt = _now.AddHours(-3),
        };
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(originalId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SentEmail sent = Assert.Single(emails.Sent);
        Assert.Equal(VerificationText, sent.Message.TextBody);
    }

    [Fact]
    public async Task ResendEmail_VerificationLinkExpired_ReturnsLinkExpiredAndSendsNothing()
    {
        DateTimeOffset occurredAt = _now.AddHours(-25);
        var original = new StoredSendLogEntry { EventId = "E101", EventOccurredAt = occurredAt };
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(originalId), null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("RESEND_LINK_EXPIRED", problem.Code);
        Assert.Equal("urn:routego:notification:problems:resend-link-expired", problem.Type);
        Assert.Equal(occurredAt.AddHours(24), problem.LinkExpiredAt);
        Assert.Empty(emails.Sent);
        Assert.Single(await SendLogStore.EntriesForEventAsync(_database, "E101"));
    }

    [Fact]
    public async Task ResendEmail_EntryWithNoRecipient_ReturnsResendNotPossibleAndSendsNothing()
    {
        StoredSendLogEntry original = StoredSendLogEntry.Failed("missingRecipient") with
        {
            EventId = "E130",
            ToAddress = null,
            Subject = null,
            BodyHtml = null,
            BodyText = null,
            FailureDetail = "email is missing",
        };
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(originalId), null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("RESEND_NOT_POSSIBLE", problem.Code);
        Assert.Equal("missingRecipient", problem.FailureReason);
        Assert.Equal("This email cannot be resent: no recipient.", problem.Detail);
        Assert.Empty(emails.Sent);
        Assert.Single(await SendLogStore.EntriesForEventAsync(_database, "E130"));
    }

    [Fact]
    public async Task ResendEmail_UnknownEntry_ReturnsNotFound()
    {
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("NOT_FOUND", problem.Code);
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task ResendEmail_MalformedEntryIdentifier_ReturnsValidationProblem()
    {
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync("internal/sendLogEntries/abc/resend", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.NotNull(problem.Errors);
        Assert.Contains("sendLogEntryId", problem.Errors.Keys);
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task ResendEmail_EmailServiceUnavailable_RecordsFailedEntryAndReturnsUpstreamUnavailable()
    {
        Guid originalId = await SendLogStore.InsertAsync(_database, OperatorApproved("E105"));
        var emails = new FakeEmailSender(EmailSendResult.Unavailable("ServiceUnavailable: try later"));
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(originalId), null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("UPSTREAM_UNAVAILABLE", problem.Code);
        Assert.Equal(true, problem.Retryable);
        Assert.NotNull(problem.SendLogEntryId);
        SendLogEntryRow? newEntry = await SendLogStore.EntryAsync(_database, problem.SendLogEntryId.Value);
        Assert.NotNull(newEntry);
        Assert.Equal("failed", newEntry.Status);
        Assert.Equal("emailServiceUnavailable", newEntry.FailureReason);
        Assert.Equal(originalId, newEntry.ResendOf);
        Assert.Single(emails.Sent);
    }

    [Fact]
    public async Task ResendEmail_EmailServiceRefuses_RecordsFailedEntryAndReturnsEmailRefused()
    {
        Guid originalId = await SendLogStore.InsertAsync(_database, OperatorApproved("E105"));
        var emails = new FakeEmailSender(EmailSendResult.Refused("Email address is not verified."));
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(originalId), null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("EMAIL_REFUSED", problem.Code);
        Assert.Equal("Email address is not verified.", problem.ProviderReason);
        Assert.NotNull(problem.SendLogEntryId);
        SendLogEntryRow? newEntry = await SendLogStore.EntryAsync(_database, problem.SendLogEntryId.Value);
        Assert.NotNull(newEntry);
        Assert.Equal("failed", newEntry.Status);
        Assert.Equal("refusedByEmailService", newEntry.FailureReason);
        Assert.Equal("Email address is not verified.", newEntry.FailureDetail);
        Assert.Equal(originalId, newEntry.ResendOf);
    }

    [Fact]
    public async Task ResendEmail_EntryThatIsItselfAResend_RecordsNewEntryPointingAtIt()
    {
        Guid firstId = await SendLogStore.InsertAsync(_database, OperatorApproved("E105"));
        StoredSendLogEntry resend = OperatorApproved("E105") with { ResendOf = firstId, AttemptedAt = _now.AddHours(-1) };
        Guid resendId = await SendLogStore.InsertAsync(_database, resend);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await InternalApiHost.CreateClient(host).PostAsync(ResendPath(resendId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ResendEmailResponse newEntry = await HttpJson.ReadAsync<ResendEmailResponse>(response);
        Assert.Equal(resendId, newEntry.ResendOf);
        IReadOnlyList<SendLogEntryRow> entries = await SendLogStore.EntriesForEventAsync(_database, "E105");
        Assert.Equal(3, entries.Count);
    }

    private static string ResendPath(Guid sendLogEntryId)
    {
        return $"internal/sendLogEntries/{sendLogEntryId}/resend";
    }

    private static StoredSendLogEntry FailedOperatorApproved(string eventId)
    {
        return OperatorApproved(eventId) with
        {
            Status = "failed",
            FailureReason = "emailServiceUnavailable",
            FailureDetail = "Throttling",
            ProviderMessageId = null,
            SentAt = null,
        };
    }

    private static StoredSendLogEntry OperatorApproved(string eventId)
    {
        return new StoredSendLogEntry
        {
            EventId = eventId,
            EventType = "operatorApproved",
            EmailType = "operatorApproved",
            ToAddress = "owner@busco.in",
            Subject = "Your RouteGo operator application is approved",
            BodyHtml = "<p>Hi Anil Shah,</p>",
            BodyText = "Hi Anil Shah,",
            CorrelationId = $"corr-{eventId}",
        };
    }
}
