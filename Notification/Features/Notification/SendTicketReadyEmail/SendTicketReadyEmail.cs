namespace Notification.Features.Notification.SendTicketReadyEmail;

/// <summary>
/// Tells the booking contact that the booking's e-ticket is ready, or that a new one replaces the
/// earlier one after a seat change, with a link to the ticket page. The ticket is identified by its
/// booking and ticket change time, not by the event ID, because the same ticket may be announced
/// twice under different event IDs.
/// </summary>
public sealed partial class SendTicketReadyEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "TicketReady";

    private const string FirstIssue = "FirstIssue";
    private const string Reissue = "Reissue";

    private readonly EmailSendRecorder _recorder;
    private readonly PortalLinks _links;

    public SendTicketReadyEmailHandler(EmailSendRecorder recorder, PortalLinks links)
    {
        _recorder = recorder;
        _links = links;
    }

    /// <summary>
    /// Sends the ticket email for one TicketReady event and says whether the message is done with
    /// or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        var facts = BookingFacts.Read(receivedEvent.Data);
        string? issueKind = EventData.Text(receivedEvent.Data, "issueKind");
        DateTimeOffset? changedAt = EventData.Time(receivedEvent.Data, "ticketChangeAt");
        EmailDraft draft = BuildDraft(receivedEvent, facts, issueKind, changedAt);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private EmailDraft BuildDraft(
        ReceivedEvent receivedEvent, BookingFacts facts, string? issueKind, DateTimeOffset? changedAt)
    {
        var draft = new EmailDraft
        {
            EmailType = ChooseEmailType(issueKind),
            EventType = EventType.TicketReady,
            RepeatKey = ChooseRepeatKey(receivedEvent, facts.BookingId, changedAt),
            BookingId = facts.BookingId,
            DepartsAt = facts.DepartsAt,
            ToAddress = facts.ContactEmail,
            IsStoppedByCancelledBooking = true,
        };

        if (facts.ContactEmail is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "contact email is missing");
        }

        string? problem = DescribeUnusableData(facts, issueKind, changedAt);
        if (problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, problem);
        }

        Uri ticketPage = _links.TicketPage(facts.BookingId!.Value);
        return draft with { Message = BuildTicketEmail(facts, draft.EmailType, ticketPage) };
    }

    // An unusable issue kind still records as ticket ready, so the failed entry can be found.
    private static EmailType ChooseEmailType(string? issueKind)
    {
        if (issueKind == Reissue)
        {
            return EmailType.TicketReissued;
        }

        return EmailType.TicketReady;
    }

    // The booking and ticket change time name the ticket; an event too incomplete to name one falls
    // back to its own ID so its failed entry is still recorded once.
    private static string ChooseRepeatKey(ReceivedEvent receivedEvent, Guid? bookingId, DateTimeOffset? changedAt)
    {
        if (bookingId is null || changedAt is null)
        {
            return receivedEvent.EventId;
        }

        string changedAtUtc = changedAt.Value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return $"{bookingId.Value:D}|{changedAtUtc}";
    }

    private static string? DescribeUnusableData(BookingFacts facts, string? issueKind, DateTimeOffset? changedAt)
    {
        bool isKnownKind = issueKind == FirstIssue || issueKind == Reissue;
        var check = new FieldCheck();
        check.Require("bookingId", facts.BookingId != null);
        check.Require("contact name", facts.ContactName != null);
        check.Require("issueKind (FirstIssue or Reissue)", isKnownKind);
        check.Require("ticketChangeAt", changedAt != null);
        check.Require("trip source", facts.Source != null);
        check.Require("trip destination", facts.Destination != null);
        check.Require("departure time", facts.DepartsAt != null);
        return check.Problem;
    }
}
