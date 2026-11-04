namespace Notification.Features.Notification.SendTicketReadyEmail;

// The wording of the ticket ready and ticket reissued emails, word for word as the feature spec authors it.
public sealed partial class SendTicketReadyEmailHandler
{
    private static EmailMessage BuildTicketEmail(BookingFacts facts, EmailType emailType, Uri ticketPage)
    {
        DateTimeOffset departsAt = facts.DepartsAt!.Value;
        string bookingId = facts.BookingId!.Value.ToString("D");
        string travelDate = EmailFormats.Date(departsAt);
        string route = $"{facts.Source} to {facts.Destination} on {travelDate}";

        var email = new EmailMessageBuilder();
        email.Paragraph($"Hi {facts.ContactName},");
        if (emailType == EmailType.TicketReissued)
        {
            email.Paragraph(
                $"Your booking {bookingId} has changed, so we have made a new e-ticket. "
                + "It replaces the ticket we sent you before — please use this one when you board.");
        }
        else
        {
            email.Paragraph($"Your e-ticket for booking {bookingId} is ready.");
        }

        email.Paragraph(BookingEmailLines.Route(facts.Source!, facts.Destination!), BookingEmailLines.Departure(departsAt));
        email.Button("Open my ticket", ticketPage.ToString());
        AddSignInAdvice(email, emailType);

        if (emailType == EmailType.TicketReissued)
        {
            return email.Build($"Your updated RouteGo e-ticket: {route}");
        }

        return email.Build($"Your RouteGo e-ticket: {route}");
    }

    private static void AddSignInAdvice(EmailMessageBuilder email, EmailType emailType)
    {
        if (emailType == EmailType.TicketReissued)
        {
            email.Paragraph("Sign in to RouteGo to open it.");
            return;
        }

        email.Paragraph("Sign in to RouteGo to open it. Please show your ticket, on your phone or printed, when you board.");
    }
}
