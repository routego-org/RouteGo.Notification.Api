using System.Text.Json;

namespace Notification.Infrastructure.Messaging;

public sealed class EmailServiceReportReaderTests
{
    private const string PermanentBounce = """
        {"notificationType":"Bounce",
         "bounce":{"bounceType":"Permanent","bounceSubType":"General",
                   "bouncedRecipients":[{"emailAddress":"ravi@gmial.com","diagnosticCode":"smtp; 550 5.1.1 user unknown"}],
                   "timestamp":"2026-11-05T04:31:00.000Z"},
         "mail":{"messageId":"R-777"}}
        """;

    [Fact]
    public void Read_PermanentBounce_GivesTheAddressTimeReceiptAndDetail()
    {
        EmailServiceReportReading reading = EmailServiceReportReader.Read(PermanentBounce);

        EmailServiceReport report = Assert.IsType<EmailServiceReport>(reading.Report);
        Assert.Equal(ReportKind.PermanentBounce, report.Kind);
        Assert.Equal("ravi@gmial.com", Assert.Single(report.Addresses));
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 4, 31, 0, TimeSpan.Zero), report.ReportedAt);
        Assert.Equal("R-777", report.ProviderMessageId);
        Assert.Equal("smtp; 550 5.1.1 user unknown", report.Detail);
    }

    [Fact]
    public void Read_ComplaintWrappedInATopicMessage_GivesTheComplaint()
    {
        string complaint = """
            {"notificationType":"Complaint",
             "complaint":{"complainedRecipients":[{"emailAddress":"sita@gmail.com"}],
                          "timestamp":"2026-11-06T09:00:00.000Z","complaintFeedbackType":"abuse"},
             "mail":{"messageId":"R-888"}}
            """;
        string wrapped = JsonSerializer.Serialize(new { Type = "Notification", Message = complaint });

        EmailServiceReportReading reading = EmailServiceReportReader.Read(wrapped);

        EmailServiceReport report = Assert.IsType<EmailServiceReport>(reading.Report);
        Assert.Equal(ReportKind.Complaint, report.Kind);
        Assert.Equal("sita@gmail.com", Assert.Single(report.Addresses));
        Assert.Equal("abuse", report.Detail);
        Assert.Equal("R-888", report.ProviderMessageId);
    }

    [Fact]
    public void Read_TransientBounce_IsATemporaryBounce()
    {
        string transient = PermanentBounce.Replace("Permanent", "Transient");

        EmailServiceReportReading reading = EmailServiceReportReader.Read(transient);

        EmailServiceReport report = Assert.IsType<EmailServiceReport>(reading.Report);
        Assert.Equal(ReportKind.TemporaryBounce, report.Kind);
    }

    [Fact]
    public void Read_BounceNamingNoAddress_IsNotUsable()
    {
        string noAddress = """{"notificationType":"Bounce","bounce":{"bounceType":"Permanent","bouncedRecipients":[],"timestamp":"2026-11-05T04:31:00Z"}}""";

        EmailServiceReportReading reading = EmailServiceReportReader.Read(noAddress);

        Assert.Null(reading.Report);
        Assert.Equal("the report names no address", reading.Problem);
    }

    [Fact]
    public void Read_DeliveryNotice_IsNotUsable()
    {
        EmailServiceReportReading reading = EmailServiceReportReader.Read("""{"notificationType":"Delivery","delivery":{}}""");

        Assert.Null(reading.Report);
        Assert.Equal("the report kind is not a bounce or a complaint", reading.Problem);
    }

    [Fact]
    public void Read_NotJson_IsNotUsable()
    {
        EmailServiceReportReading reading = EmailServiceReportReader.Read("this is not json");

        Assert.Null(reading.Report);
        Assert.Equal("the message is not JSON", reading.Problem);
    }

    [Fact]
    public void Read_BounceWithoutATime_IsNotUsable()
    {
        string noTime = PermanentBounce.Replace("\"timestamp\":\"2026-11-05T04:31:00.000Z\"", "\"feedbackId\":\"f\"");

        EmailServiceReportReading reading = EmailServiceReportReader.Read(noTime);

        Assert.Null(reading.Report);
        Assert.Equal("the report has no time", reading.Problem);
    }
}
