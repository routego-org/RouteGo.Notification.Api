namespace Notification.Features.Notification.SendVerificationEmail;

// The two wordings of the verification email, word for word as the feature spec authors them.
public sealed partial class SendVerificationEmailHandler
{
    private static EmailMessage BuildCustomerEmail(string fullName, string verificationLink)
    {
        var email = new EmailMessageBuilder();
        email.Paragraph($"Hi {fullName},");
        email.Paragraph("Welcome to RouteGo! Please verify your email address to start booking bus tickets.");
        email.Button("Verify my email", verificationLink);
        email.Paragraph(
            "This link works for 24 hours and can be used only once.",
            "If you did not create a RouteGo account, you can ignore this email.");
        return email.Build("Verify your email for RouteGo");
    }

    private static EmailMessage BuildOperatorEmail(string fullName, string verificationLink)
    {
        var email = new EmailMessageBuilder();
        email.Paragraph($"Hi {fullName},");
        email.Paragraph("Thank you for applying to sell bus tickets on RouteGo. Please verify your email address.");
        email.Button("Verify my email", verificationLink);
        email.Paragraph(
            "This link works for 24 hours and can be used only once.",
            "After you verify, our team will review your application. We will email you when it is approved.",
            "If you did not apply, you can ignore this email.");
        return email.Build("Verify your email for your RouteGo operator application");
    }
}
