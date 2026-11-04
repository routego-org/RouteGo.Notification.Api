namespace Notification.Features.Notification.SendCancellationEmail;

[Collection(DatabaseCollection.Name)]
public sealed class SendCancellationEmailTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;

    public SendCancellationEmailTests(DatabaseFixture database)
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
    public async Task HandleAsync_CustomerCancelledWithRefund_SaysAsYouAskedAndShowsRefundWithoutReason()
    {
        EmailTestHost host = NewHost();

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Cancelled("E50", "CustomerCancelled", null, 50, 650), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Equal("ravi@gmail.com", sent.ToAddress);
        Assert.Equal("Booking cancelled: Mumbai to Pune on 12 Nov 2026", sent.Message.Subject);
        Assert.Contains($"As you asked, your booking {TestBooking.DefaultId} has been cancelled.", sent.Message.TextBody);
        Assert.Contains("Refund: ₹650 (50% of what you paid). We will email you again when the refund is complete.", sent.Message.TextBody);
        Assert.DoesNotContain("Reason:", sent.Message.TextBody);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E50"));
        Assert.Equal("bookingCancelled", entry.EmailType);
        Assert.Equal("bookingCancelled", entry.EventType);
        Assert.Equal("sent", entry.Status);
        Assert.Equal(TestBooking.DefaultId, entry.BookingId);
    }

    [Fact]
    public async Task HandleAsync_OperatorCancelled_ShowsTheOperatorsReasonAndFullRefund()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(
            Cancelled("E51", "OperatorCancelled", "Bus breakdown", 100, 1300), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Contains("The bus operator has cancelled this trip, so your booking", sent.Message.TextBody);
        Assert.Contains("Reason: Bus breakdown", sent.Message.TextBody);
        Assert.Contains("Refund: ₹1,300 (100% of what you paid).", sent.Message.TextBody);
    }

    [Fact]
    public async Task HandleAsync_AdminCancelled_ShowsTheAdminsReason()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(
            Cancelled("E52", "AdminCancelled", "Duplicate booking found by support", 100, 1300), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Contains($"RouteGo support has cancelled your booking {TestBooking.DefaultId}.", sent.Message.TextBody);
        Assert.Contains("Reason: Duplicate booking found by support", sent.Message.TextBody);
    }

    [Fact]
    public async Task HandleAsync_AdminCancelledWithoutReason_LeavesTheReasonLineOut()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Cancelled("E52", "AdminCancelled", null, 100, 1300), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.DoesNotContain("Reason:", sent.Message.TextBody);
    }

    [Fact]
    public async Task HandleAsync_NoRefundDue_SaysNoRefundAndPromisesNoFurtherEmail()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Cancelled("E53", "CustomerCancelled", null, 0, 0), CancellationToken.None);

        SentEmail sent = Assert.Single(host.Emails.Sent);
        Assert.Contains("No refund is due for this cancellation.", sent.Message.TextBody);
        Assert.DoesNotContain("We will email you again", sent.Message.TextBody);
    }

    [Fact]
    public async Task HandleAsync_BookingKnownWithPlannedReminder_MarksItCancelledAndWithdrawsTheReminder()
    {
        var known = new StoredKnownBooking
        {
            ReminderState = "planned",
            ReminderDueAt = TestBooking.DefaultDeparture.AddHours(-24),
        };
        await KnownBookingFixtures.InsertAsync(_database, known);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Cancelled("E50", "CustomerCancelled", null, 50, 650), CancellationToken.None);

        KnownBookingRow stored = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("cancelled", stored.Status);
        Assert.Equal("withdrawn", stored.ReminderState);
        Assert.Equal<DateTime?>(TestEvents.OccurredAt.UtcDateTime, stored.CancelledAt);
    }

    [Fact]
    public async Task HandleAsync_CancellationBeforeConfirmation_BecomesKnownAsCancelledWithNoReminder()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Cancelled("E54", "CustomerCancelled", null, 50, 650), CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        KnownBookingRow stored = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("cancelled", stored.Status);
        Assert.Equal("notPlanned", stored.ReminderState);
    }

    [Fact]
    public async Task HandleAsync_SameEventDeliveredTwice_SendsOneEmailAndKeepsTheBooking()
    {
        EmailTestHost host = NewHost();
        SendCancellationEmailHandler handler = NewHandler(host);
        ReceivedEvent cancelled = Cancelled("E50", "CustomerCancelled", null, 50, 650);
        await handler.HandleAsync(cancelled, CancellationToken.None);

        await handler.HandleAsync(cancelled, CancellationToken.None);

        Assert.Single(host.Emails.Sent);
        Assert.Single(await EntriesAsync("E50"));
    }

    [Fact]
    public async Task HandleAsync_NoContactEmail_LogsFailedButStillWithdrawsTheKnownBooking()
    {
        await KnownBookingFixtures.InsertAsync(_database, new StoredKnownBooking());
        EmailTestHost host = NewHost();
        var booking = new TestBooking { ContactEmail = null };

        await NewHandler(host).HandleAsync(Cancelled("E55", "CustomerCancelled", null, 50, 650, booking), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E55"));
        Assert.Equal("failed", entry.Status);
        Assert.Equal("missingRecipient", entry.FailureReason);
        KnownBookingRow stored = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("cancelled", stored.Status);
    }

    [Fact]
    public async Task HandleAsync_UnknownKind_LogsFailedInvalidEventDataButStillRecordsTheCancellation()
    {
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Cancelled("E56", "Unknown", null, 50, 650), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E56"));
        Assert.Equal("invalidEventData", entry.FailureReason);
        KnownBookingRow stored = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("cancelled", stored.Status);
    }

    [Fact]
    public async Task HandleAsync_BookingAlreadyKnownCancelled_StillSendsTheCancellationEmail()
    {
        await KnownBookingFixtures.InsertCancelledAsync(_database, TestBooking.DefaultId);
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Cancelled("E50", "CustomerCancelled", null, 50, 650), CancellationToken.None);

        Assert.Single(host.Emails.Sent);
    }

    [Fact]
    public async Task HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress()
    {
        await KnownBookingFixtures.SuppressAsync(_database, "ravi@gmail.com");
        EmailTestHost host = NewHost();

        await NewHandler(host).HandleAsync(Cancelled("E57", "CustomerCancelled", null, 50, 650), CancellationToken.None);

        Assert.Empty(host.Emails.Sent);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E57"));
        Assert.Equal("skipped", entry.Status);
        Assert.Equal("suppressedAddress", entry.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_LastAttemptStillUnavailable_ParksAndTheReminderIsAlreadyWithdrawn()
    {
        var known = new StoredKnownBooking
        {
            ReminderState = "planned",
            ReminderDueAt = TestBooking.DefaultDeparture.AddHours(-24),
        };
        await KnownBookingFixtures.InsertAsync(_database, known);
        EmailTestHost host = NewHost(EmailSendResult.Unavailable("ServiceUnavailable: try later"));

        EventHandlingOutcome outcome = await NewHandler(host).HandleAsync(
            Cancelled("E50", "CustomerCancelled", null, 50, 650, isLastAttempt: true), CancellationToken.None);

        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        SendLogEntryRow entry = Assert.Single(await EntriesAsync("E50"));
        Assert.Equal("emailServiceUnavailable", entry.FailureReason);
        FakeLogRecord alert = Assert.IsType<FakeLogRecord>(host.ParkedAlert());
        Assert.Contains("EMAIL_SEND_PARKED", alert.Message);
        Assert.Contains("E50", alert.Message);
        KnownBookingRow stored = Assert.IsType<KnownBookingRow>(await KnownBookingFixtures.GetAsync(_database, TestBooking.DefaultId));
        Assert.Equal("withdrawn", stored.ReminderState);
    }

    private EmailTestHost NewHost(params EmailSendResult[] results)
    {
        return new EmailTestHost(_database, new FakeEmailSender(results));
    }

    private static SendCancellationEmailHandler NewHandler(EmailTestHost host)
    {
        return new SendCancellationEmailHandler(host.Recorder, host.KnownBookings);
    }

    private static ReceivedEvent Cancelled(
        string eventId, string kind, string? reason, int refundPercent, int refundAmount,
        TestBooking? booking = null, bool isLastAttempt = false)
    {
        TestBooking cancelledBooking = booking ?? new TestBooking();
        var extras = new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["reason"] = reason,
            ["refundPercent"] = refundPercent,
            ["refundAmount"] = refundAmount,
        };
        return TestEvents.Create(SendCancellationEmailHandler.TriggerEventType, eventId, cancelledBooking.Data(extras), isLastAttempt);
    }

    private Task<IReadOnlyList<SendLogEntryRow>> EntriesAsync(string eventId)
    {
        return SendLogStore.EntriesForEventAsync(_database, eventId);
    }
}
