namespace Notification.Features.Notification.RemoveSuppressedAddress;

[Collection(DatabaseCollection.Name)]
public sealed class RemoveSuppressedAddressTests : IAsyncLifetime
{
    private const string BasePath = "/internal/suppressedAddresses";

    private readonly DatabaseFixture _database;
    private readonly ApiFactory _host;

    public RemoveSuppressedAddressTests(DatabaseFixture database)
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
    public async Task RemoveSuppressedAddress_ExistingEntry_ReturnsNoContentAndTheAddressIsEmailedAgain()
    {
        Guid id = await KnownBookingFixtures.InsertSuppressedAsync(_database, new StoredSuppressedAddress());
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Empty(await KnownBookingFixtures.ListSuppressedAsync(_database));
    }

    [Fact]
    public async Task RemoveSuppressedAddress_OnlyThatEntry_LeavesTheOthers()
    {
        Guid ravi = await KnownBookingFixtures.InsertSuppressedAsync(_database, new StoredSuppressedAddress());
        await KnownBookingFixtures.InsertSuppressedAsync(_database, new StoredSuppressedAddress { EmailAddress = "sita@gmail.com" });
        HttpClient client = InternalApiHost.CreateClient(_host);

        await client.DeleteAsync($"{BasePath}/{ravi}");

        SuppressedAddressRow remaining = Assert.Single(await KnownBookingFixtures.ListSuppressedAsync(_database));
        Assert.Equal("sita@gmail.com", remaining.EmailAddress);
    }

    [Fact]
    public async Task RemoveSuppressedAddress_UnknownOrAlreadyRemovedEntry_ReturnsNotFound()
    {
        Guid id = await KnownBookingFixtures.InsertSuppressedAsync(_database, new StoredSuppressedAddress());
        HttpClient client = InternalApiHost.CreateClient(_host);
        await client.DeleteAsync($"{BasePath}/{id}");

        HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("NOT_FOUND", problem.Code);
    }

    [Fact]
    public async Task RemoveSuppressedAddress_MalformedIdentifier_ReturnsValidationProblem()
    {
        HttpClient client = InternalApiHost.CreateClient(_host);

        HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.NotNull(problem.Errors);
        Assert.Contains("suppressedAddressId", problem.Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task RemoveSuppressedAddress_InternalKeyMissingOrWrong_ReturnsUnauthorizedAndChangesNothing(string? internalKey)
    {
        Guid id = await KnownBookingFixtures.InsertSuppressedAsync(_database, new StoredSuppressedAddress());
        HttpClient client = InternalApiHost.CreateClient(_host, internalKey);

        HttpResponseMessage response = await client.DeleteAsync($"{BasePath}/{id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(await KnownBookingFixtures.ListSuppressedAsync(_database));
    }
}
