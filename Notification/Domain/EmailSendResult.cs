namespace Notification.Domain;

/// <summary>
/// The outcome of one send, with the email service's receipt number when it accepted the email,
/// or the detail of the problem when it did not.
/// </summary>
public sealed record EmailSendResult(EmailSendOutcome Outcome, string? ProviderMessageId, string? Detail)
{
    /// <summary>The email service accepted the email.</summary>
    public static EmailSendResult Accepted(string providerMessageId)
    {
        return new EmailSendResult(EmailSendOutcome.Accepted, providerMessageId, null);
    }

    /// <summary>The email service refused the email for good, for the given reason.</summary>
    public static EmailSendResult Refused(string reason)
    {
        return new EmailSendResult(EmailSendOutcome.Refused, null, reason);
    }

    /// <summary>The email service could not take the email right now, with the error seen.</summary>
    public static EmailSendResult Unavailable(string error)
    {
        return new EmailSendResult(EmailSendOutcome.Unavailable, null, error);
    }
}
