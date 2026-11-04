namespace Notification.Infrastructure;

/// <summary>
/// The name of every environment-supplied configuration value, spelled exactly once. The names are
/// deliberately FLAT: each maps 1:1 to an environment variable of the same spelling, so no ':' or
/// '__' separator can be mistyped between the deployment template, the pipeline, and the reader
/// here. LocalEntryPoints and every consumer go through these constants, so a rename is a compile
/// error rather than a value that silently arrives null (references/configuration.md).
/// </summary>
public static class ConfigKeys
{
    /// <summary>The AWS SDK's own region variable, and the ONLY region source the app reads.
    /// Lambda sets it; LocalEntryPoints seeds it on a laptop. There is deliberately no app-owned
    /// AWS:Region key that could disagree with the runtime's own value.</summary>
    public const string AwsRegion = "AWS_REGION";

    // ══════════════════════════════════════════════════════════════════════════
    // FILLED BY the sibling slice skill and by feature work: one const per value
    // that varies by environment — a base URL, a client id, a public origin.
    //
    //   public const string BackendBaseUrl = "BackendBaseUrl";
    //
    // Adding one is four more edits, none of them a new file: a local value in
    // LocalEntryPoints, a parameter + env var in the deployment template, a
    // pipeline variable, and a RequireKeys entry if the app cannot start without
    // it. NEVER add a configuration key to an appsettings*.json.
    //
    // Otel:Exporter:* and AWS:Profile do NOT belong here — see
    // references/configuration.md for why those two keep their nested names.
    // ══════════════════════════════════════════════════════════════════════════
    /// <summary>Which Lambda execution model this process runs on — Lambda (one invocation per process,
    /// frozen between them) or ManagedInstances (many concurrent invocations in one always-on process).
    /// It picks the database pool profile (Infrastructure/Pgsql/ConnectionProfile). Set by the deployment
    /// template, never sniffed from the runtime. Required.</summary>
    public const string DbConnectionProfile = "DB_CONNECTION_PROFILE";

    /// <summary>The most database connections one process may hold: 2-4 on Lambda (a handler's opens are
    /// sequential), at least the per-environment concurrency on ManagedInstances. Set by the deployment
    /// template beside DbConnectionProfile. Required.</summary>
    public const string DbMaxPoolSize = "DB_MAX_POOL_SIZE";

    /// <summary>The AWS SDK's own variable for sending every AWS call to one endpoint. Unset when
    /// deployed (the SDK uses the real AWS endpoints); LocalStack's address on a laptop.</summary>
    public const string AwsEndpointUrl = "AWS_ENDPOINT_URL";

    /// <summary>The URL of the SQS queue the email workers read (notification-q). Required.</summary>
    public const string NotificationQueueUrl = "NotificationQueueUrl";

    /// <summary>The address every email is sent from, for example noreply@your-domain. It must be a
    /// sender identity verified in the email service. Required.</summary>
    public const string SenderAddress = "SenderAddress";

    /// <summary>The display name shown with the sender address. Optional: RouteGo when unset.</summary>
    public const string SenderDisplayName = "SenderDisplayName";

    /// <summary>The Portal's base address, for example https://portal.routego.in — links in emails
    /// start from it. Required.</summary>
    public const string PortalBaseUrl = "PortalBaseUrl";

    /// <summary>The path of the Portal's sign-in page, joined to PortalBaseUrl for the sign-in link
    /// in the operator approved email (the Portal's own route, /auth/login today). Required.</summary>
    public const string PortalSignInPath = "PortalSignInPath";

    /// <summary>The URL of the SQS queue the email service's bounce and complaint reports arrive on
    /// (the email-service-reports queue). Required.</summary>
    public const string EmailServiceReportsQueueUrl = "EmailServiceReportsQueueUrl";

    /// <summary>How many hours before departure the departure reminder is due. Applies to bookings
    /// confirmed after a change. Optional: 24 when unset.</summary>
    public const string ReminderLeadHours = "ReminderLeadHours";

    /// <summary>How many days a rejected operator waits before applying again, as the operator
    /// rejected email states it. Must equal Identity's re-application rule. Optional: 7 when unset.</summary>
    public const string OperatorReapplicationWaitDays = "OperatorReapplicationWaitDays";

    /// <summary>How many hours a verification link stays usable after its event occurred. A
    /// verification email older than this is not resent. Must equal Identity's setting of the same
    /// name. Optional: 24 when unset.</summary>
    public const string VerificationLinkLifetimeHours = "VerificationLinkLifetimeHours";
}
