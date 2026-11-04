namespace Notification.Features.Notification.SendBookingFailedEmail;

// The wording of the booking failed email, word for word as the feature spec authors it.
public sealed partial class SendBookingFailedEmailHandler
{
    // A cancelled trip means nothing was paid; lost seats mean the payment is being returned.
    private static EmailMessage BuildFailedEmail(BookingFacts facts, bool arePaidSeatsLost)
    {
        DateTimeOffset departsAt = facts.DepartsAt!.Value;
        string bookingId = facts.BookingId!.Value.ToString("D");
        string greeting = "Hello,";
        if (facts.ContactName != null)
        {
            greeting = $"Hi {facts.ContactName},";
        }

        string openingLine = $"The bus operator has cancelled the trip, so your booking {bookingId} was not completed. You have not been charged.";
        if (arePaidSeatsLost)
        {
            openingLine = $"We received your payment, but your seats could no longer be confirmed, so your booking {bookingId} was not completed. "
                + "We are returning your full payment and will email you when it has been returned.";
        }

        var email = new EmailMessageBuilder();
        email.Paragraph(greeting);
        email.Paragraph(openingLine);
        email.Paragraph(BookingEmailLines.Route(facts.Source!, facts.Destination!), BookingEmailLines.Departure(departsAt));
        email.Paragraph("You are welcome to book another bus on RouteGo.");
        string travelDate = EmailFormats.Date(departsAt);
        return email.Build($"Booking not completed: {facts.Source} to {facts.Destination} on {travelDate}");
    }
}
