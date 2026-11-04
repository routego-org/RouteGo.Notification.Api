namespace Notification.Domain;

/// <summary>How one attempt to hand an email to the email service ended.</summary>
public enum EmailSendOutcome
{
    /// <summary>The email service accepted the email and returned its receipt number.</summary>
    Accepted,

    /// <summary>The email service refused the email for good; sending it again as it is cannot succeed.</summary>
    Refused,

    /// <summary>The email service was down, busy, slow or not allowed to send; a later attempt may succeed.</summary>
    Unavailable,
}
