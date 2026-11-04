namespace Notification.Domain;

/// <summary>What the email service reports happened to an email after it accepted it.</summary>
public enum ReportKind
{
    /// <summary>The email service could never deliver to the address: it does not exist.</summary>
    PermanentBounce,

    /// <summary>A delivery problem that may pass, for example a full mailbox.</summary>
    TemporaryBounce,

    /// <summary>The address's owner marked a RouteGo email as spam.</summary>
    Complaint,
}

/// <summary>One bounce or complaint report from the email service, as the do-not-send list needs it.</summary>
public sealed record EmailServiceReport
{
    /// <summary>What happened.</summary>
    public required ReportKind Kind { get; init; }

    /// <summary>The addresses the report concerns; at least one.</summary>
    public required IReadOnlyList<string> Addresses { get; init; }

    /// <summary>When the bounce or complaint happened, in UTC.</summary>
    public required DateTimeOffset ReportedAt { get; init; }

    /// <summary>The email service's receipt number of the reported email, when the report names it.</summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>The email service's own description of the bounce or complaint.</summary>
    public string? Detail { get; init; }
}
