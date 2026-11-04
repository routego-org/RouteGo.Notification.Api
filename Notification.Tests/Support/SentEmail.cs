namespace Notification.Tests.Support;

/// <summary>One email the fake email service was handed.</summary>
public sealed record SentEmail(string ToAddress, EmailMessage Message);
