namespace Notification.Common.Ports;

/// <summary>
/// The email service: delivers one email from the configured sender to one recipient, and says
/// whether it was accepted, refused for good, or could not be reached right now.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends <paramref name="message"/> to <paramref name="toAddress"/> and names the outcome.</summary>
    Task<EmailSendResult> SendAsync(string toAddress, EmailMessage message, CancellationToken cancellationToken);
}
