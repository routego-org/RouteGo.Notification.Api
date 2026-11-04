namespace Notification.Features.Notification.SendSeatsChangedEmail;

// The wording of the seats changed email, word for word as the feature spec authors it.
public sealed partial class SendSeatsChangedEmailHandler
{
    private static EmailMessage BuildSeatsChangedEmail(BookingFacts facts, IReadOnlyList<SeatMove> moves, Uri ticketPage)
    {
        DateTimeOffset departsAt = facts.DepartsAt!.Value;
        string bookingId = facts.BookingId!.Value.ToString("D");
        string[] moveLines = moves.Select(move => $"Seat {move.FromSeat} → Seat {move.ToSeat}").ToArray();

        var email = new EmailMessageBuilder();
        email.Paragraph($"Hi {facts.ContactName},");
        email.Paragraph($"The seats on your booking {bookingId} have changed:");
        email.Paragraph(moveLines);
        email.Paragraph("Your seats are now:");
        email.Paragraph(BookingEmailLines.SeatLines(facts.Seats));
        email.Paragraph(BookingEmailLines.Route(facts.Source!, facts.Destination!), BookingEmailLines.Departure(departsAt));
        email.Paragraph("Your fare is unchanged. Your updated e-ticket will follow in a separate email in a few minutes.");
        email.Button("View my ticket", ticketPage.ToString());
        string travelDate = EmailFormats.Date(departsAt);
        return email.Build($"Your seats have changed: {facts.Source} to {facts.Destination} on {travelDate}");
    }
}
