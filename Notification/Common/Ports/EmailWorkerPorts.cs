namespace Notification.Common.Ports;

/// <summary>
/// The ports every slice that sends an email is built from — the email workers and the resend
/// operation alike: the send log writer, the email service and the clock.
/// </summary>
public sealed record EmailWorkerPorts(IWriterConnectionFactory Writer, IEmailSender EmailSender, IClock Clock);
