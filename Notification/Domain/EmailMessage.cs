namespace Notification.Domain;

/// <summary>One email exactly as it is sent: the subject and the two versions of the message.</summary>
public sealed record EmailMessage(string Subject, string HtmlBody, string TextBody);
