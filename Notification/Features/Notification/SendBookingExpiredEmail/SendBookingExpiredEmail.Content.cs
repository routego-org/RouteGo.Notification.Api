namespace Notification.Features.Notification.SendBookingExpiredEmail;

// The wording of the booking expired email, word for word as the feature spec authors it.
public sealed partial class SendBookingExpiredEmailHandler
{
    private static EmailMessage BuildExpiredEmail(BookingFacts facts)
    {
        DateTimeOffset departsAt = facts.DepartsAt!.Value;
        string bookingId = facts.BookingId!.Value.ToString("D");
        string greeting = "Hello,";
        if (facts.ContactName != null)
        {
            greeting = $"Hi {facts.ContactName},";
        }

        var email = new EmailMessageBuilder();
        email.Paragraph(greeting);
        email.Paragraph(
            $"We held your seats for booking {bookingId}, but payment did not arrive in time, "
            + "so the seats have been released and the booking was not completed. You have not been charged.");
        email.Paragraph(BookingEmailLines.Route(facts.Source!, facts.Destination!), BookingEmailLines.Departure(departsAt));
        email.Paragraph("If you still want to travel, you are welcome to book again on RouteGo while seats remain.");
        string travelDate = EmailFormats.Date(departsAt);
        return email.Build($"Seats released: {facts.Source} to {facts.Destination} on {travelDate}");
    }
}
