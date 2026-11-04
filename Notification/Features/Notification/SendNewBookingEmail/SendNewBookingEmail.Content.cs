namespace Notification.Features.Notification.SendNewBookingEmail;

// The wording of the new booking email, word for word as the feature spec authors it.
public sealed partial class SendNewBookingEmailHandler
{
    private static EmailMessage BuildNewBookingEmail(BookingFacts facts, Uri operatorTrips)
    {
        DateTimeOffset departsAt = facts.DepartsAt!.Value;
        var email = new EmailMessageBuilder();
        email.Paragraph($"Hello {facts.OperatorName},");
        email.Paragraph("A new booking has been made on your trip.");
        email.Paragraph(
            $"Booking ID: {facts.BookingId!.Value:D}",
            BookingEmailLines.Route(facts.Source!, facts.Destination!),
            BookingEmailLines.Departure(departsAt),
            $"Bus: {facts.BusName}");
        email.Paragraph(BookingEmailLines.PassengerLines(facts.Seats));
        email.Paragraph("You can see every passenger of this trip in RouteGo.");
        email.Button("View my trips", operatorTrips.ToString());
        string travelDate = EmailFormats.Date(departsAt);
        string bookingId = facts.BookingId.Value.ToString("D");
        return email.Build($"New booking {bookingId}: {facts.Source} to {facts.Destination} on {travelDate}");
    }
}
