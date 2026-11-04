namespace Notification.Features.Notification.SendBookingConfirmedEmail;

// The wording of the booking confirmed email, word for word as the feature spec authors it.
public sealed partial class SendBookingConfirmedEmailHandler
{
    private static EmailMessage BuildConfirmedEmail(BookingFacts facts, Uri ticketPage)
    {
        DateTimeOffset departsAt = facts.DepartsAt!.Value;
        string airConditioning = "Non-AC";
        if (facts.IsAc == true)
        {
            airConditioning = "AC";
        }

        var email = new EmailMessageBuilder();
        email.Paragraph($"Hi {facts.ContactName},");
        email.Paragraph("Your booking is confirmed.");
        email.Paragraph(
            $"Booking ID: {facts.BookingId!.Value:D}",
            BookingEmailLines.Route(facts.Source!, facts.Destination!),
            BookingEmailLines.Departure(departsAt),
            $"Bus: {facts.BusName} ({facts.BusType}, {airConditioning})");
        email.Paragraph(BookingEmailLines.SeatLines(facts.Seats));
        email.Paragraph($"Total paid: {EmailFormats.Rupees(facts.TotalAmount!.Value)}");
        email.Paragraph("Your e-ticket will follow in a separate email in a few minutes. You can also open it any time:");
        email.Button("View my ticket", ticketPage.ToString());
        string travelDate = EmailFormats.Date(departsAt);
        return email.Build($"Booking confirmed: {facts.Source} to {facts.Destination} on {travelDate}");
    }
}
