namespace Notification.Domain;

/// <summary>
/// The rules every email Notification sends from an event follows: which repeat deliveries are
/// dropped, and when a temporary failure stops being retried.
/// </summary>
public static class SendLogRules
{
    /// <summary>
    /// Whether a repeat delivery of the same event finds nothing left to do. Only an email already
    /// accepted by the email service blocks a repeat; an attempt still sending or failed is tried
    /// again on the same entry, so a temporary failure or a replay is never a lost email.
    /// </summary>
    public static bool BlocksRepeat(SendStatus existingStatus)
    {
        return existingStatus == SendStatus.Sent;
    }

    /// <summary>
    /// Whether this delivery is the queue's last automatic attempt: after it, the queue parks the
    /// message instead of delivering it again.
    /// </summary>
    public static bool IsLastAttempt(int receiveCount, int maxReceiveCount)
    {
        return receiveCount >= maxReceiveCount;
    }
}
