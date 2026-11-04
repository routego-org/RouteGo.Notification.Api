namespace Notification.Features.Notification.SendOperatorRejectedEmail;

/// <summary>
/// Tells a rejected operator by email why its application was not approved and when it can apply
/// again, and records every attempt in the send log. A repeat delivery of an event whose email is
/// already sent sends nothing; an earlier attempt that is still sending, failed or skipped is tried
/// again on the same entry. An address on the do-not-send list is not emailed.
/// </summary>
public sealed partial class SendOperatorRejectedEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "OperatorRejected";

    private readonly EmailSendRecorder _recorder;

    public SendOperatorRejectedEmailHandler(EmailSendRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Sends the rejected email for one OperatorRejected event, naming the configured re-application
    /// wait, and says whether the message is done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(
        ReceivedEvent receivedEvent, int reapplicationWaitDays, CancellationToken cancellationToken)
    {
        EmailDraft draft = BuildDraft(receivedEvent, reapplicationWaitDays);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private static EmailDraft BuildDraft(ReceivedEvent receivedEvent, int reapplicationWaitDays)
    {
        string? email = EventData.Text(receivedEvent.Data, "email");
        string? reason = EventData.Text(receivedEvent.Data, "reason");
        var draft = new EmailDraft
        {
            EmailType = EmailType.OperatorRejected,
            EventType = EventType.OperatorRejected,
            RepeatKey = receivedEvent.EventId,
            ToAddress = email,
        };

        if (email is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "email is missing");
        }

        if (reason is null)
        {
            return draft.Fail(FailureReason.InvalidEventData, "reason is missing");
        }

        return draft with { Message = BuildRejectedEmail(reason, reapplicationWaitDays) };
    }
}
