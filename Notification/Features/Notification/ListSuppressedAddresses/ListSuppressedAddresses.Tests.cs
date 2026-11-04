namespace Notification.Features.Notification.ListSuppressedAddresses;

[Collection(DatabaseCollection.Name)]
public sealed class ListSuppressedAddressesTests : IAsyncLifetime
{
    private const string ListPath = "/internal/suppressedAddresses";

    private static readonly DateTimeOffset _added5Nov = new DateTimeOffset(2026, 11, 5, 4, 31, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _added6Nov = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;
    private readonly ApiFactory _host;

    public ListSuppressedAddressesTests(DatabaseFixture database)
    {
        _database = database;
        _host = InternalApiHost.Create(database, new FakeEmailSender(), new FixedClock(EmailTestHost.Now));
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
    public async Task ListSuppressedAddresses_TwoAddresses_ReturnsBothNewestFirstWithEveryField()
    {
        Guid ravi = await SeedAsync(new StoredSuppressedAddress { SuppressedAt = _added5Nov, ReportedAt = _added5Nov });
        Guid sita = await SeedAsync(new StoredSuppressedAddress
        {
            EmailAddress = "sita@gmail.com",
            Reason = "complaint",
            ProviderMessageId = null,
            ReportDetail = null,
            SuppressedAt = _added6Nov,
            ReportedAt = _added6Nov,
        });
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ListSuppressedAddressesResponse page = await HttpJson.ReadAsync<ListSuppressedAddressesResponse>(response);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { sita, ravi }, page.Items.Select(item => item.SuppressedAddressId));
        SuppressedAddressEntry second = page.Items[1];
        Assert.Equal("ravi@gmial.com", second.EmailAddress);
        Assert.Equal(SuppressionReason.PermanentBounce, second.Reason);
        Assert.Equal("R-777", second.ProviderMessageId);
        Assert.Equal("smtp; 550 5.1.1 user unknown", second.ReportDetail);
        Assert.Equal(_added5Nov, second.ReportedAt);
        Assert.Equal(_added5Nov, second.SuppressedAt);
        Assert.Equal(SuppressionReason.Complaint, page.Items[0].Reason);
    }

    [Fact]
    public async Task ListSuppressedAddresses_AddressInDifferentCase_ReturnsExactlyThatEntry()
    {
        Guid ravi = await SeedAsync(new StoredSuppressedAddress());
        await SeedAsync(new StoredSuppressedAddress { EmailAddress = "sita@gmail.com" });
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{ListPath}?emailAddress=Ravi@Gmial.com");

        ListSuppressedAddressesResponse page = await HttpJson.ReadAsync<ListSuppressedAddressesResponse>(response);
        SuppressedAddressEntry entry = Assert.Single(page.Items);
        Assert.Equal(ravi, entry.SuppressedAddressId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task ListSuppressedAddresses_AddressNotOnTheList_ReturnsAnEmptyPage()
    {
        await SeedAsync(new StoredSuppressedAddress());
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{ListPath}?emailAddress=nobody@example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ListSuppressedAddressesResponse page = await HttpJson.ReadAsync<ListSuppressedAddressesResponse>(response);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task ListSuppressedAddresses_Page2WithPageSize1_ReturnsTheOlderEntry()
    {
        Guid ravi = await SeedAsync(new StoredSuppressedAddress { SuppressedAt = _added5Nov });
        await SeedAsync(new StoredSuppressedAddress { EmailAddress = "sita@gmail.com", SuppressedAt = _added6Nov });
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{ListPath}?page=2&pageSize=1");

        ListSuppressedAddressesResponse page = await HttpJson.ReadAsync<ListSuppressedAddressesResponse>(response);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ravi, Assert.Single(page.Items).SuppressedAddressId);
    }

    [Fact]
    public async Task ListSuppressedAddresses_MalformedAddress_ReturnsValidationProblemNamingTheAddress()
    {
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{ListPath}?emailAddress=not-an-email");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.NotNull(problem.Errors);
        Assert.Contains("emailAddress", problem.Errors.Keys);
    }

    [Fact]
    public async Task ListSuppressedAddresses_PageSizeOver200_ReturnsValidationProblemNamingPageSize()
    {
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.GetAsync($"{ListPath}?pageSize=201");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.NotNull(problem.Errors);
        Assert.Contains("pageSize", problem.Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task ListSuppressedAddresses_InternalKeyMissingOrWrong_ReturnsUnauthorizedWithNothing(string? internalKey)
    {
        await SeedAsync(new StoredSuppressedAddress());
        HttpClient client = InternalApiHost.CreateClient(_host, internalKey);

        HttpResponseMessage response = await client.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ravi@gmial.com", body);
        Assert.Equal("UNAUTHORIZED", HttpJson.Deserialize<ProblemBody>(body).Code);
    }

    private Task<Guid> SeedAsync(StoredSuppressedAddress address)
    {
        return KnownBookingFixtures.InsertSuppressedAsync(_database, address);
    }
}
