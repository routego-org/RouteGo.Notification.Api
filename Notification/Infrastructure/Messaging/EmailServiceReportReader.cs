namespace Notification.Infrastructure.Messaging;

/// <summary>
/// The result of reading one report message: the report when it names a recognised kind, at least
/// one address and a time, or what is wrong with the message when it does not.
/// </summary>
public sealed record EmailServiceReportReading(EmailServiceReport? Report, string? Problem);

/// <summary>
/// Reads the email service's bounce and complaint notifications as they arrive on the reports
/// queue, whether the queue delivers the notification itself or wrapped in the topic message that
/// carried it. Anything else about the email's delivery is not a report this app uses.
/// </summary>
public static class EmailServiceReportReader
{
    private const string BounceType = "Bounce";
    private const string ComplaintType = "Complaint";
    private const string PermanentBounceKind = "Permanent";

    /// <summary>Reads the message body.</summary>
    public static EmailServiceReportReading Read(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            JsonElement notification = Unwrap(document.RootElement);
            return ReadNotification(notification);
        }
        catch (JsonException)
        {
            return new EmailServiceReportReading(null, "the message is not JSON");
        }
    }

    // A topic delivers the notification as the text of its Message member.
    private static JsonElement Unwrap(JsonElement root)
    {
        string? inner = EventData.Text(root, "Message");
        if (inner is null || EventData.Text(root, "Type") != "Notification")
        {
            return root;
        }

        using var innerDocument = JsonDocument.Parse(inner);
        return innerDocument.RootElement.Clone();
    }

    private static EmailServiceReportReading ReadNotification(JsonElement notification)
    {
        string? type = EventData.Text(notification, "notificationType");
        if (type is null)
        {
            type = EventData.Text(notification, "eventType");
        }

        string? providerMessageId = EventData.Text(EventData.Child(notification, "mail"), "messageId");
        if (type == BounceType)
        {
            JsonElement bounce = EventData.Child(notification, "bounce");
            ReportKind kind = BounceKind(EventData.Text(bounce, "bounceType"));
            return Complete(kind, bounce, "bouncedRecipients", providerMessageId);
        }

        if (type == ComplaintType)
        {
            JsonElement complaint = EventData.Child(notification, "complaint");
            return Complete(ReportKind.Complaint, complaint, "complainedRecipients", providerMessageId);
        }

        return new EmailServiceReportReading(null, "the report kind is not a bounce or a complaint");
    }

    // Only a permanent bounce is a permanent bounce; an undetermined one is not worth a list entry.
    private static ReportKind BounceKind(string? bounceType)
    {
        if (bounceType == PermanentBounceKind)
        {
            return ReportKind.PermanentBounce;
        }

        return ReportKind.TemporaryBounce;
    }

    private static EmailServiceReportReading Complete(
        ReportKind kind, JsonElement details, string recipientsName, string? providerMessageId)
    {
        var addresses = new List<string>();
        string? detail = null;
        foreach (JsonElement recipient in EventData.Items(details, recipientsName))
        {
            string? address = EventData.Text(recipient, "emailAddress");
            if (address != null)
            {
                addresses.Add(address.Trim());
            }

            if (detail is null)
            {
                detail = EventData.Text(recipient, "diagnosticCode");
            }
        }

        if (addresses.Count == 0)
        {
            return new EmailServiceReportReading(null, "the report names no address");
        }

        DateTimeOffset? reportedAt = EventData.Time(details, "timestamp");
        if (reportedAt is null)
        {
            return new EmailServiceReportReading(null, "the report has no time");
        }

        if (detail is null)
        {
            detail = EventData.Text(details, "complaintFeedbackType");
        }

        var report = new EmailServiceReport
        {
            Kind = kind,
            Addresses = addresses,
            ReportedAt = reportedAt.Value,
            ProviderMessageId = providerMessageId,
            Detail = detail,
        };
        return new EmailServiceReportReading(report, null);
    }
}
