namespace Notification.Features.Notification.SendSeatsChangedEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendSeatsChangedEmailTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;

    public SendSeatsChangedEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_SeatMoved_SendsMoveAndNewSeatsWithTicketLink()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            SeatsChanged("E40", MovedBooking(), 1), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal("Your seats have changed: Mumbai to Pune on 12 Nov 2026", sent.Message.Subject);
        Assert.Contains("Seat 12 → Seat 14", sent.Message.TextBody);
        Assert.Contains("Seat 13 – Asha Kumar", sent.Message.TextBody);
        Assert.Contains("Seat 14 – Ravi Kumar", sent.Message.TextBody);
        Assert.Contains("Your fare is unchanged.", sent.Message.TextBody);
        Assert.Contains($"https://portal.routego.in/tickets/{TestBooking.DefaultId}", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E40"));
        Assert.Equal("seatsChanged", entry.EmailType);
        Assert.Equal("bookingSeatsChanged", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal(TestBooking.DefaultId, entry.BookingId);
    }

    [Fact]
    public async Task HandleAsync_NewerSeatChange_KnownBookingTakesTheNewSeats()
    {
        await KnownBookingFixtures.InsertAsync(_database, new StoredKnownBooking());
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(SeatsChanged("E40", MovedBooking(), 1), CancellationToken.None);

        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("13, 14", known.SeatLabels);
        Assert.Equal(1, known.SeatChangeCount);
    }

    [Fact]
    public async Task HandleAsync_OlderSeatChangeArrivingLate_KeepsNewerSeatsButStillSendsEmail()
    {
        var known = new StoredKnownBooking { SeatLabels = "13, 15", SeatChangeCount = 2 };
        await KnownBookingFixtures.InsertAsync(_database, known);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(SeatsChanged("E41", MovedBooking(), 1), CancellationToken.None);

        KnownBookingRow stored = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("13, 15", stored.SeatLabels);
        Assert.Equal(2, stored.SeatChangeCount);
        Assert.Single(host.Emails.Sent);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmailAndKeepsTheBooking()
    {
        EmailTestHost host = NewHost();
        SendSeatsChangedEmailHandler handler = NewHandler(host);
        ReceivedEvent changed = SeatsChanged("E40", MovedBooking(), 1);
        await handler.HandleAsync(changed, CancellationToken.None);

        await handler.HandleAsync(changed, CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal(1, known.SeatChangeCount);
    }

    [Fact]
    public async Task HandleAsync_BookingNotKnownYet_BecomesKnownAsConfirmedFromTheEvent()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(SeatsChanged("E42", MovedBooking(), 1), CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("confirmed", known.Status);
        Assert.Equal("13, 14", known.SeatLabels);
        Assert.Equal("ravi@gmail.com", known.ContactEmail);
    }

    [Fact]
    public async Task HandleAsync_NoContactEmail_LogsFailedMissingRecipient()
    {
        EmailTestHost host = NewHost();
        TestBooking booking = MovedBooking() with { ContactEmail = null };

        await NewHandler(host).HandleAsync(SeatsChanged("E43", booking, 1), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E43"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        Assert.False(host.RaisedWarningOrAlert());
    }

    [Fact]
    public async Task HandleAsync_NoMoves_LogsFailedInvalidEventData()
    {
        EmailTestHost host = NewHost();
        var extras = new Dictionary<string, object?> { ["seatChangeCount"] = 1 };
        ReceivedEvent noMoves = TestEvents.Create(
            SendSeatsChangedEmailHandler.TriggerEventType, "E44", MovedBooking().Data(extras));

        await NewHandler(host).HandleAsync(noMoves, CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E44"));
        Assert.Equal("invalidEventData", entry.FailureReason);
        Assert.Equal("changes is missing", entry.FailureDetail);
    }

    [Fact]
    public async Task HandleAsync_BookingKnownToBeCancelled_SkipsAndStaysCancelled()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(SeatsChanged("E45", MovedBooking(), 1), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E45"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("bookingCancelled", entry.SkipReason);
        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("cancelled", known.Status);
    }

    [Fact]
    public async Task HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress()
    {
        await KnownBookingFixtures.SuppressAsync(_database, "ravi@gmail.com");
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(SeatsChanged("E46", MovedBooking(), 1), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E46"));
        Assert.Equal("suppressedAddress", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndKnownBookingAlreadyHasNewSeats()
    {
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            SeatsChanged("E40", MovedBooking(), 1, isLastAttempt: true), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E40"));
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E40", alert.Message);
        KnownBookingRow known = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("13, 14", known.SeatLabels);
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendSeatsChangedEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendSeatsChangedEmailHandler(host.Recorder, host.KnownBookings, host.Links);
    }

    // Ravi moved from seat 12 to seat 14, so the booking now holds seats 13 (Asha) and 14 (Ravi).
    private static TestBooking MovedBooking()
    {
        return new TestBooking
        {
            Seats = new List<TestSeat>
            {
                new TestSeat("13", "Asha Kumar", 31, "Female"),
                new TestSeat("14", "Ravi Kumar", 34, "Male"),
            },
        };
    }

    private static ReceivedEvent SeatsChanged(string eventId, TestBooking booking, int seatChangeCount, bool isLastAttempt = false)
    {
        var move = new Dictionary<string, object?> { ["fromSeatNo"] = "12", ["toSeatNo"] = "14" };
        var extras = new Dictionary<string, object?>
        {
            ["changes"] = new List<Dictionary<string, object?>> { move },
            ["seatChangeCount"] = seatChangeCount,
        };
        return TestEvents.Create(SendSeatsChangedEmailHandler.TriggerEventType, eventId, booking.Data(extras), isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
