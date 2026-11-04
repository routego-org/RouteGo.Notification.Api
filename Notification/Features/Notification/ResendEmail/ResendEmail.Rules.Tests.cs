namespace Notification.Features.Notification.ResendEmail;

// The resend rules that depend on the booking, the trip and the do-not-send list.
[Collection(DatabaseCollection.Name)]
public sealed class ResendEmailRulesTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly FixedClock _clock = new FixedClock(EmailTestHost.Now);

    public ResendEmailRulesTests(DatabaseFixture database)
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
    public async Task ResendEmail_BookingEmailOfAKnownCancelledBooking_ReturnsBookingCancelledAndSendsNothing()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        Guid originalId = await SendLogStore.InsertAsync(_database, BookingEmail("bookingConfirmed", "E20"));
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await ResendAsync(host, originalId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("RESEND_BOOKING_CANCELLED", problem.Code);
        Assert.Equal(TestBooking.DefaultId, problem.BookingId);
        Assert.Empty(emails.Sent);
        Assert.Single(await SendLogStore.EntriesForEventAsync(_database, "E20"));
    }

    [Theory]
    [InlineData("bookingCancelled", "bookingCancelled")]
    [InlineData("refundCompleted", "refundCompleted")]
    public async Task ResendEmail_CancellationOrRefundEmailOfACancelledBooking_IsResent(string emailType, string eventType)
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        StoredSendLogEntry original = BookingEmail(emailType, "E21") with { EventType = eventType };
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await ResendAsync(host, originalId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(emails.Sent);
    }

    [Theory]
    [InlineData("ticketReady")]
    [InlineData("seatsChanged")]
    [InlineData("departureReminder")]
    [InlineData("bookingFailed")]
    [InlineData("bookingExpired")]
    public async Task ResendEmail_TripEmailAfterDeparture_ReturnsTripDeparted(string emailType)
    {
        DateTimeOffset departed = EmailTestHost.Now.AddHours(-3);
        StoredSendLogEntry original = BookingEmail(emailType, "E22") with { DepartsAt = departed };
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await ResendAsync(host, originalId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("RESEND_TRIP_DEPARTED", problem.Code);
        Assert.Equal(departed, problem.DepartsAt);
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task ResendEmail_CancellationEmailAfterDeparture_IsStillResent()
    {
        StoredSendLogEntry original = BookingEmail("bookingCancelled", "E23") with { DepartsAt = EmailTestHost.Now.AddHours(-3) };
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await ResendAsync(host, originalId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResendEmail_AddressOnDoNotSendList_ReturnsAddressSuppressedWithTheReason()
    {
        await KnownBookingFixtures.InsertSuppressedAsync(
            _database, new StoredSuppressedAddress { EmailAddress = "Ravi@Gmail.com", Reason = "complaint" });
        Guid originalId = await SendLogStore.InsertAsync(_database, BookingEmail("bookingConfirmed", "E24"));
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await ResendAsync(host, originalId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("ADDRESS_SUPPRESSED", problem.Code);
        Assert.Equal("ravi@gmail.com", problem.ToAddress);
        Assert.Equal("complaint", problem.SuppressionReason);
        Assert.Empty(emails.Sent);
        Assert.Single(await SendLogStore.EntriesForEventAsync(_database, "E24"));
    }

    [Fact]
    public async Task ResendEmail_SkippedEntryAfterTheAddressWasRemoved_IsSentAndKeepsTheBookingDetails()
    {
        StoredSendLogEntry skipped = BookingEmail("bookingConfirmed", "E25") with
        {
            Status = "skipped",
            SkipReason = "suppressedAddress",
            ProviderMessageId = null,
            SentAt = null,
        };
        Guid originalId = await SendLogStore.InsertAsync(_database, skipped);
        var emails = new FakeEmailSender();
        await using ApiFactory host = InternalApiHost.Create(_database, emails, _clock);

        HttpResponseMessage response = await ResendAsync(host, originalId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ResendEmailResponse newEntry = await HttpJson.ReadAsync<ResendEmailResponse>(response);
        Assert.Equal(SendStatus.Sent, newEntry.Status);
        Assert.Equal(originalId, newEntry.ResendOf);
        Assert.Equal(TestBooking.DefaultId, newEntry.BookingId);
        Assert.Equal(TestBooking.DefaultDeparture, newEntry.DepartsAt);
        Assert.Null(newEntry.SkipReason);
        SendLogEntryRow stored = Assert.IsType<SendLogEntryRow>(await SendLogStore.EntryAsync(_database, newEntry.SendLogEntryId));
        Assert.Equal("E25", stored.RepeatKey);
        Assert.Equal(TestBooking.DefaultId, stored.BookingId);
    }

    [Fact]
    public async Task ResendEmail_CancelledAndDepartedAndSuppressed_ReportsTheCancellationFirst()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        await KnownBookingFixtures.SuppressAsync(_database, "ravi@gmail.com");
        StoredSendLogEntry original = BookingEmail("ticketReady", "E26") with { DepartsAt = EmailTestHost.Now.AddHours(-3) };
        Guid originalId = await SendLogStore.InsertAsync(_database, original);
        await using ApiFactory host = InternalApiHost.Create(_database, new FakeEmailSender(), _clock);

        HttpResponseMessage response = await ResendAsync(host, originalId);

        ProblemBody problem = await HttpJson.ReadProblemAsync(response);
        Assert.Equal("RESEND_BOOKING_CANCELLED", problem.Code);
    }

    private static Task<HttpResponseMessage> ResendAsync(ApiFactory host, Guid sendLogEntryId)
    {
        HttpClient client = InternalApiHost.CreateClient(host);
        return client.PostAsync($"/internal/sendLogEntries/{sendLogEntryId}/resend", null);
    }

    // A Sent booking email to ravi@gmail.com about the default booking and its trip.
    private static StoredSendLogEntry BookingEmail(string emailType, string eventId)
    {
        return new StoredSendLogEntry
        {
            EventId = eventId,
            EventType = "bookingConfirmed",
            EmailType = emailType,
            BookingId = TestBooking.DefaultId,
            DepartsAt = TestBooking.DefaultDeparture,
            Subject = "Booking confirmed: Mumbai to Pune on 12 Nov 2026",
            BodyHtml = "<p>Your booking is confirmed.</p>",
            BodyText = "Your booking is confirmed.",
            CorrelationId = $"corr-{eventId}",
        };
    }
}
