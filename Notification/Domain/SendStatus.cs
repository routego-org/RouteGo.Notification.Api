namespace Notification.Domain;

/// <summary>Where a send log entry stands.</summary>
public enum SendStatus
{
    /// <summary>Being sent; only temporary failures so far.</summary>
    Sending,

    /// <summary>The email service accepted the email.</summary>
    Sent,

    /// <summary>It will not be retried on its own.</summary>
    Failed,

    /// <summary>A rule decided the email must not be sent; the reason is the entry's skip reason.</summary>
    Skipped,
}
