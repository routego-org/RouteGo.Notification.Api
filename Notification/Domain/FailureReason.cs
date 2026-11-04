namespace Notification.Domain;

/// <summary>Why a send log entry failed. Present exactly when its status is failed.</summary>
public enum FailureReason
{
    /// <summary>The event named no recipient email address.</summary>
    MissingRecipient,

    /// <summary>A field the email needs was missing or unusable.</summary>
    InvalidEventData,

    /// <summary>The email service refused the email for good.</summary>
    RefusedByEmailService,

    /// <summary>The email service stayed unavailable through every automatic retry.</summary>
    EmailServiceUnavailable,
}
