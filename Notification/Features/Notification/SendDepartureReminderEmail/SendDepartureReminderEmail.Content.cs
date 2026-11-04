namespace Notification.Features.Notification.SendDepartureReminderEmail;

// The wording of the departure reminder email, word for word as the feature spec authors it.
public sealed partial class SendDepartureReminderEmailHandler
{
    private static EmailMessage BuildReminderEmail(Guid bookingId, ReminderBooking booking, DateTimeOffset departsAt, Uri ticketPage)
    {
        var email = new EmailMessageBuilder();
        email.Paragraph($"Hi {booking.ContactName},");
        email.Paragraph("This is a reminder that your bus leaves tomorrow.");
        email.Paragraph(
            $"Booking ID: {bookingId:D}",
            BookingEmailLines.Route(booking.Source, booking.Destination),
            BookingEmailLines.Departure(departsAt),
            $"Bus: {booking.BusName}",
            $"Seats: {booking.SeatLabels}");
        email.Paragraph("Please reach the boarding point a little early and carry your ticket, on your phone or printed.");
        email.Button("Open my ticket", ticketPage.ToString());
        email.Paragraph("Have a good journey!");
        return email.Build($"Reminder: your bus to {booking.Destination} leaves tomorrow");
    }
}
