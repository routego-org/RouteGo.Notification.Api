namespace Notification.Features.Notification.SendOperatorApprovedEmail;

// The wording of the operator approved email, word for word as the feature spec authors it.
public sealed partial class SendOperatorApprovedEmailHandler
{
    // The event carries no person's name, so the email greets without one.
    private static EmailMessage BuildApprovedEmail(string companyName, Uri signInPage)
    {
        var email = new EmailMessageBuilder();
        email.Paragraph("Hello,");
        email.Paragraph(
            $"Good news! {companyName} is now approved as a bus operator on RouteGo.",
            "You can sign in and start adding your buses, routes and trips.");
        email.Button("Sign in", signInPage.ToString());
        return email.Build("Your RouteGo operator account is approved");
    }
}
