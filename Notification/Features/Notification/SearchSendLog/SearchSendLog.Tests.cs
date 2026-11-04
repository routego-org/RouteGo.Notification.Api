namespace Notification.Features.Notification.SearchSendLog;

[Collection(DatabaseCollection.Name)]
public sealed class SearchSendLogTests : IAsyncLifetime
{
    private const string SearchPath = "internal/sendLogEntries";

    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _at1015 = new DateTimeOffset(2026, 10, 2, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _at1020 = new DateTimeOffset(2026, 10, 2, 10, 20, 0, TimeSpan.Zero);

    // Forty-five entries to one address, one minute apart: entry n is event "E{n}", so the newest
    // is E45 and the oldest E1.
    private const string ManyEntriesSql = """
        INSERT INTO notification.send_log_entries
            (event_id, repeat_key, event_type, email_type, to_address, subject, body_html, body_text, status,
             provider_message_id, event_occurred_at, correlation_id, attempted_at, sent_at)
        SELECT 'E' || n, 'E' || n, 'userRegistered', 'customerVerification', 'ravi@gmail.com', 'Verify', '<p>Hi</p>',
               'Hi', 'sent', 'ses-' || n, @Start, 'corr-' || n,
               @Start + make_interval(mins => n), @Start + make_interval(mins => n)
        FROM generate_series(1, @Count) AS n;
        """;

    private readonly DatabaseFixture _database;
    private readonly ApiFactory _host;

    public SearchSendLogTests(DatabaseFixture database)
    {
        _database = database;
        _host = InternalApiHost.Create(database, new FakeEmailSender(), new FixedClock(_now));
    }

    public async Task InitializeAsync()
    {
        await _database.ResetBusinessTablesAsync();
    }

    public Task DisposeAsync()
    {
        return _host.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task SearchSendLog_TwoAttemptsToAddress_ReturnsOnlyThoseNewestFirst()
    {
        SeededAttempts seeded = await SeedTwoAttemptsToRaviAndOneToSitaAsync();
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{SearchPath}?toAddress=ravi@gmail.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchSendLogResponse page = await HttpJson.ReadAsync<SearchSendLogResponse>(response);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { seeded.FailedId, seeded.SentId }, page.Items.Select(item => item.SendLogEntryId));
    }

    [Fact]
    public async Task SearchSendLog_TwoAttemptsToAddress_ShowsEveryRecordedFieldButNotTheMessage()
    {
        await SeedTwoAttemptsToRaviAndOneToSitaAsync();
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{SearchPath}?toAddress=ravi@gmail.com");

        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bodyHtml", body);
        Assert.DoesNotContain("bodyText", body);
        SearchSendLogResponse page = HttpJson.Deserialize<SearchSendLogResponse>(body);
        SearchSendLogEntry failed = page.Items[0];
        Assert.Equal(EmailType.CustomerVerification, failed.EmailType);
        Assert.Equal(EventType.UserRegistered, failed.EventType);
        Assert.Equal("Verify your email for RouteGo", failed.Subject);
        Assert.Equal(SendStatus.Failed, failed.Status);
        Assert.Equal(FailureReason.EmailServiceUnavailable, failed.FailureReason);
        Assert.Equal("Throttling: Maximum sending rate exceeded.", failed.FailureDetail);
        Assert.Null(failed.ProviderMessageId);
        Assert.Equal("E2", failed.EventId);
        Assert.Equal("corr-E2", failed.CorrelationId);
        Assert.Null(failed.ResendOf);
        Assert.Equal(_at1020, failed.AttemptedAt);
        Assert.Null(failed.SentAt);
        SearchSendLogEntry sent = page.Items[1];
        Assert.Equal("ses-original", sent.ProviderMessageId);
        Assert.Equal(_at1015, sent.SentAt);
        Assert.Equal(TestEvents.OccurredAt, sent.EventOccurredAt);
    }

    [Fact]
    public async Task SearchSendLog_AddressInDifferentCase_ReturnsTheEntry()
    {
        Guid entryId = await SendLogStore.InsertAsync(_database, new StoredSendLogEntry { ToAddress = "ravi@gmail.com" });
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{SearchPath}?toAddress=Ravi@Gmail.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchSendLogResponse page = await HttpJson.ReadAsync<SearchSendLogResponse>(response);
        SearchSendLogEntry entry = Assert.Single(page.Items);
        Assert.Equal(entryId, entry.SendLogEntryId);
    }

    [Fact]
    public async Task SearchSendLog_NoEmailEverToAddress_ReturnsEmptyPage()
    {
        await SendLogStore.InsertAsync(_database, new StoredSendLogEntry { ToAddress = "ravi@gmail.com" });
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{SearchPath}?toAddress=nobody@example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchSendLogResponse page = await HttpJson.ReadAsync<SearchSendLogResponse>(response);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task SearchSendLog_Page3Of45Entries_ReturnsTheOldestFive()
    {
        await _database.SeedAsync(ManyEntriesSql, new { Start = TestEvents.OccurredAt, Count = 45 });
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{SearchPath}?toAddress=ravi@gmail.com&page=3&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchSendLogResponse page = await HttpJson.ReadAsync<SearchSendLogResponse>(response);
        Assert.Equal(45, page.TotalCount);
        Assert.Equal(3, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(new[] { "E5", "E4", "E3", "E2", "E1" }, page.Items.Select(item => item.EventId));
    }

    [Theory]
    [InlineData(SearchPath)]
    [InlineData(SearchPath + "?toAddress=not-an-email")]
    public async Task SearchSendLog_AddressMissingOrMalformed_ReturnsValidationProblemNamingAddress(string path)
    {
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.NotNull(problem.Errors);
        Assert.Contains("toAddress", problem.Errors.Keys);
    }

    [Fact]
    public async Task SearchSendLog_PageSizeOver200_ReturnsValidationProblemNamingPageSize()
    {
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{SearchPath}?toAddress=ravi@gmail.com&pageSize=201");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.NotNull(problem.Errors);
        Assert.Contains("pageSize", problem.Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task SearchSendLog_InternalKeyMissingOrWrong_ReturnsUnauthorizedWithNoEntries(string? internalKey)
    {
        await SendLogStore.InsertAsync(_database, new StoredSendLogEntry { ToAddress = "ravi@gmail.com" });
        HttpClient client = InternalApiHost.CreateClient(_host, internalKey);

        HttpResponseMessage response = await client.GetAsync($"{SearchPath}?toAddress=ravi@gmail.com");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ravi@gmail.com", body);
        ProblemBody problem = HttpJson.Deserialize<ProblemBody>(body);
        Assert.Equal("UNAUTHORIZED", problem.Code);
        Assert.Equal("urn:routego:notification:problems:unauthorized", problem.Type);
        Assert.Equal(401, problem.Status);
    }

    // A Sent email to ravi@gmail.com at 10:15, a Failed one to the same address at 10:20, and a
    // Sent email to sita@gmail.com that a search for Ravi must not return.
    private async Task<SeededAttempts> SeedTwoAttemptsToRaviAndOneToSitaAsync()
    {
        Guid sentId = await SendLogStore.InsertAsync(_database, new StoredSendLogEntry
        {
            EventId = "E1",
            AttemptedAt = _at1015,
            SentAt = _at1015,
        });
        Guid failedId = await SendLogStore.InsertAsync(_database, StoredSendLogEntry.Failed("emailServiceUnavailable") with
        {
            EventId = "E2",
            CorrelationId = "corr-E2",
            FailureDetail = "Throttling: Maximum sending rate exceeded.",
            AttemptedAt = _at1020,
        });
        await SendLogStore.InsertAsync(_database, new StoredSendLogEntry { EventId = "E3", ToAddress = "sita@gmail.com" });
        return new SeededAttempts(sentId, failedId);
    }

    private sealed record SeededAttempts(Guid SentId, Guid FailedId);
}
