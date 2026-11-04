namespace Notification.Features.Notification.SendOperatorRejectedEmail;

// The wording of the operator rejected email, word for word as the feature spec authors it.
public sealed partial class SendOperatorRejectedEmailHandler
{
    // The event carries no company name and no person's name, so the email names neither. The
    // reason is shown exactly as the admin wrote it.
    private static EmailMessage BuildRejectedEmail(string reason, int reapplicationWaitDays)
    {
        string wait = $"{reapplicationWaitDays} days";
        if (reapplicationWaitDays == 1)
        {
            wait = "1 day";
        }

        var email = new EmailMessageBuilder();
        email.Paragraph("Hello,");
        email.Paragraph(
            "Thank you for applying to RouteGo. We are sorry, but we could not approve your operator application at this time.");
        email.Paragraph($"Reason: {reason}");
        email.Paragraph($"You can fix the issue and apply again after {wait}.");
        return email.Build("Update on your RouteGo operator application");
    }
}
