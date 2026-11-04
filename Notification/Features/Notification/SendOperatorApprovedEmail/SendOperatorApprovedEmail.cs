namespace Notification.Features.Notification.SendOperatorApprovedEmail;

/// <summary>
/// Tells an approved operator by email that its company can now sign in to RouteGo, and records
/// every attempt in the send log. A repeat delivery of an event whose email is already sent sends
/// nothing; an earlier attempt that is still sending, failed or skipped is tried again on the same
/// entry. An address on the do-not-send list is not emailed.
/// </summary>
public sealed partial class SendOperatorApprovedEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "OperatorApproved";

    private readonly EmailSendRecorder _recorder;

    public SendOperatorApprovedEmailHandler(EmailSendRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Sends the approved email for one OperatorApproved event, linking to the Portal's sign-in page,
    /// and says whether the message is done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(
        ReceivedEvent receivedEvent, Uri signInPage, CancellationToken cancellationToken)
    {
        EmailDraft draft = BuildDraft(receivedEvent, signInPage);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private static EmailDraft BuildDraft(ReceivedEvent receivedEvent, Uri signInPage)
    {
        string? email = EventData.Text(receivedEvent.Data, "email");
        string? companyName = EventData.Text(receivedEvent.Data, "companyName");
        var draft = new EmailDraft
        {
            EmailType = EmailType.OperatorApproved,
            EventType = EventType.OperatorApproved,
            RepeatKey = receivedEvent.EventId,
            ToAddress = email,
        };

        if (email is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "email is missing");
        }

        if (companyName is null)
        {
            return draft.Fail(FailureReason.InvalidEventData, "companyName is missing");
        }

        return draft with { Message = BuildApprovedEmail(companyName, signInPage) };
    }
}
