namespace Notification.Features.Notification.SendCancellationEmail;

// The wording of the booking cancelled email, word for word as the feature spec authors it.
public sealed partial class SendCancellationEmailHandler
{
    private static EmailMessage BuildCancellationEmail(BookingFacts facts, Cancellation cancellation)
    {
        DateTimeOffset departsAt = facts.DepartsAt!.Value;
        string bookingId = facts.BookingId!.Value.ToString("D");

        var email = new EmailMessageBuilder();
        email.Paragraph($"Hi {facts.ContactName},");
        email.Paragraph(OpeningLine(cancellation.Kind!, bookingId));
        AddReason(email, cancellation);
        email.Paragraph(BookingEmailLines.Route(facts.Source!, facts.Destination!), BookingEmailLines.Departure(departsAt));
        email.Paragraph(RefundLine(cancellation));
        string travelDate = EmailFormats.Date(departsAt);
        return email.Build($"Booking cancelled: {facts.Source} to {facts.Destination} on {travelDate}");
    }

    private static string OpeningLine(string kind, string bookingId)
    {
        if (kind == AdminCancelled)
        {
            return $"RouteGo support has cancelled your booking {bookingId}.";
        }

        if (kind == OperatorCancelled)
        {
            return $"The bus operator has cancelled this trip, so your booking {bookingId} has been cancelled.";
        }

        return $"As you asked, your booking {bookingId} has been cancelled.";
    }

    // A customer never gave a reason to show; for the others the line is left out when none came.
    private static void AddReason(EmailMessageBuilder email, Cancellation cancellation)
    {
        bool isCustomer = cancellation.Kind == CustomerCancelled;
        if (isCustomer || cancellation.Reason is null)
        {
            return;
        }

        email.Paragraph($"Reason: {cancellation.Reason}");
    }

    private static string RefundLine(Cancellation cancellation)
    {
        long amount = cancellation.RefundAmount!.Value;
        if (amount == 0)
        {
            return "No refund is due for this cancellation.";
        }

        return $"Refund: {EmailFormats.Rupees(amount)} ({cancellation.RefundPercent}% of what you paid). "
            + "We will email you again when the refund is complete.";
    }
}
