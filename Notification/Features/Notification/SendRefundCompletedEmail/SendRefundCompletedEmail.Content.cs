namespace Notification.Features.Notification.SendRefundCompletedEmail;

// The wording of the refund completed email, word for word as the feature spec authors it.
public sealed partial class SendRefundCompletedEmailHandler
{
    // Payment sends no name, so a booking Notification never knew is greeted without one.
    private static EmailMessage BuildRefundEmail(Guid bookingId, long amount, KnownContact? known)
    {
        string refundAmount = EmailFormats.Rupees(amount);
        string greeting = "Hello,";
        if (known != null)
        {
            greeting = $"Hi {known.Name},";
        }

        var email = new EmailMessageBuilder();
        email.Paragraph(greeting);
        email.Paragraph($"We have returned {refundAmount} for your booking {bookingId:D} to the account you paid from.");
        email.Paragraph("Depending on your bank, it can take a few working days to show in your account.");
        return email.Build($"Your RouteGo refund of {refundAmount} is complete");
    }
}
