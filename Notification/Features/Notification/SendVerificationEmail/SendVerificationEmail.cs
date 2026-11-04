namespace Notification.Features.Notification.SendVerificationEmail;

/// <summary>
/// Emails the verification link of a newly registered customer or operator, in the wording for the
/// account's role, and records every attempt in the send log. A repeat delivery of an event whose
/// email is already sent sends nothing; an earlier attempt that is still sending, failed or skipped
/// is tried again on the same entry. An address on the do-not-send list is not emailed.
/// </summary>
public sealed partial class SendVerificationEmailHandler
{
    /// <summary>The producer's event type that starts this email.</summary>
    public const string TriggerEventType = "UserRegistered";

    private const string CustomerRole = "Customer";
    private const string OperatorRole = "Operator";

    private readonly EmailSendRecorder _recorder;

    public SendVerificationEmailHandler(EmailSendRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Sends the verification email for one UserRegistered event and says whether the message is
    /// done with or must be delivered again.
    /// </summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        EmailDraft draft = BuildDraft(receivedEvent);
        return await _recorder.SendAsync(receivedEvent, draft, cancellationToken);
    }

    private static EmailDraft BuildDraft(ReceivedEvent receivedEvent)
    {
        JsonElement data = receivedEvent.Data;
        string? email = EventData.Text(data, "email");
        string? fullName = EventData.Text(data, "name");
        string? role = EventData.Text(data, "role");
        string? verificationLink = EventData.Text(data, "verificationLink");
        var draft = new EmailDraft
        {
            EmailType = ChooseEmailType(role),
            EventType = EventType.UserRegistered,
            RepeatKey = receivedEvent.EventId,
            ToAddress = email,
        };

        if (email is null)
        {
            return draft.Fail(FailureReason.MissingRecipient, "email is missing");
        }

        string? problem = DescribeUnusableData(fullName, role, verificationLink);
        if (problem != null)
        {
            return draft.Fail(FailureReason.InvalidEventData, problem);
        }

        EmailMessage message = BuildCustomerEmail(fullName!, verificationLink!);
        if (draft.EmailType == EmailType.OperatorVerification)
        {
            message = BuildOperatorEmail(fullName!, verificationLink!);
        }

        return draft with { Message = message };
    }

    // An unusable role still records as customer verification: the email type UserRegistered
    // normally leads to, so the failed entry can be found and its reason read.
    private static EmailType ChooseEmailType(string? role)
    {
        if (string.Equals(role, OperatorRole, StringComparison.OrdinalIgnoreCase))
        {
            return EmailType.OperatorVerification;
        }

        return EmailType.CustomerVerification;
    }

    // The detail names the field, never its value: the verification link must not reach the log.
    private static string? DescribeUnusableData(string? fullName, string? role, string? verificationLink)
    {
        if (fullName is null)
        {
            return "name is missing";
        }

        if (role is null)
        {
            return "role is missing";
        }

        bool isCustomer = string.Equals(role, CustomerRole, StringComparison.OrdinalIgnoreCase);
        bool isOperator = string.Equals(role, OperatorRole, StringComparison.OrdinalIgnoreCase);
        if (!isCustomer && !isOperator)
        {
            return $"role '{role}' is not Customer or Operator";
        }

        if (verificationLink is null)
        {
            return "verificationLink is missing";
        }

        return null;
    }
}
