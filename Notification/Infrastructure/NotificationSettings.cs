namespace Notification.Infrastructure;

/// <summary>
/// The settings the email slices are parameterised by, read once at startup so a wrong value stops
/// the app instead of putting a wrong promise in every email: the Portal links, the wait a rejected
/// operator is told, and how long before departure the departure reminder is due.
/// </summary>
public sealed class NotificationSettings
{
    private const int DefaultReapplicationWaitDays = 7;
    private const int DefaultReminderLeadHours = 24;

    private NotificationSettings(PortalLinks links, int reapplicationWaitDays, TimeSpan reminderLeadTime)
    {
        Links = links;
        ReapplicationWaitDays = reapplicationWaitDays;
        ReminderLeadTime = reminderLeadTime;
    }

    /// <summary>The Portal pages emails link to.</summary>
    public PortalLinks Links { get; }

    /// <summary>How many days a rejected operator waits before applying again.</summary>
    public int ReapplicationWaitDays { get; }

    /// <summary>How long before departure the departure reminder is due.</summary>
    public TimeSpan ReminderLeadTime { get; }

    /// <summary>Reads and checks every setting; throws naming the setting that is wrong.</summary>
    public static NotificationSettings FromConfiguration(IConfiguration configuration)
    {
        // The sign-in page is the Portal's base address followed by its sign-in path; a base
        // address carrying a path keeps it, because the path is joined relative to it.
        Uri portalBase = configuration.RequireBaseAddress(ConfigKeys.PortalBaseUrl);
        string signInPath = configuration[ConfigKeys.PortalSignInPath]!.TrimStart('/');
        var links = new PortalLinks(portalBase, new Uri(portalBase, signInPath));

        int waitDays = ReadWholeNumber(configuration, ConfigKeys.OperatorReapplicationWaitDays, DefaultReapplicationWaitDays, "days");
        int leadHours = ReadWholeNumber(configuration, ConfigKeys.ReminderLeadHours, DefaultReminderLeadHours, "hours");
        return new NotificationSettings(links, waitDays, TimeSpan.FromHours(leadHours));
    }

    // A value that is not a positive whole number would put a wrong promise in every email, so it
    // stops the app at startup.
    private static int ReadWholeNumber(IConfiguration configuration, string key, int defaultValue, string unit)
    {
        string? text = configuration[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        bool isNumber = int.TryParse(text, out int value);
        if (!isNumber || value < 1)
        {
            throw new InvalidOperationException($"{key} must be a whole number of {unit}, 1 or more; it is '{text}'.");
        }

        return value;
    }
}
