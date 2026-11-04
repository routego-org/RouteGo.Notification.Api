namespace Notification.Features.Notification.ResendEmail;

/// <summary>Which send log entry to resend, exactly as the caller gave it in the route.</summary>
public sealed record ResendEmailRequest(string? SendLogEntryId);

/// <summary>The new entry recorded for the resend, in status sent.</summary>
public sealed record ResendEmailResponse
{
    /// <summary>The new entry's identifier.</summary>
    public required Guid SendLogEntryId { get; init; }

    /// <summary>The event ID of the resent entry; a resend keeps the original's value.</summary>
    public required string EventId { get; init; }

    /// <summary>The event that caused the original email.</summary>
    public required EventType EventType { get; init; }

    /// <summary>Which email this is.</summary>
    public required EmailType EmailType { get; init; }

    /// <summary>The booking the email is about; null for the Identity emails.</summary>
    public Guid? BookingId { get; init; }

    /// <summary>The trip's departure time (UTC) for an email about a trip, when known; otherwise null.</summary>
    public DateTimeOffset? DepartsAt { get; init; }

    /// <summary>The recipient the email was sent to again.</summary>
    public string? ToAddress { get; init; }

    /// <summary>The subject exactly as sent.</summary>
    public string? Subject { get; init; }

    /// <summary>Where the attempt stands: sent, on success.</summary>
    public required SendStatus Status { get; init; }

    /// <summary>Why the attempt failed; null on success.</summary>
    public FailureReason? FailureReason { get; init; }

    /// <summary>Why the attempt was skipped; null on success.</summary>
    public SkipReason? SkipReason { get; init; }

    /// <summary>Detail of the last problem; null on success.</summary>
    public string? FailureDetail { get; init; }

    /// <summary>The email service's receipt number.</summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>The entry this entry resends.</summary>
    public Guid? ResendOf { get; init; }

    /// <summary>When the original event occurred (UTC).</summary>
    public required DateTimeOffset EventOccurredAt { get; init; }

    /// <summary>The original event's correlation ID.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>When this attempt was recorded (UTC).</summary>
    public required DateTimeOffset AttemptedAt { get; init; }

    /// <summary>When the email service accepted the email (UTC).</summary>
    public DateTimeOffset? SentAt { get; init; }
}

/// <summary>How a resend ended. Each member is one answer the caller can get.</summary>
public enum ResendEmailOutcome
{
    /// <summary>The email was sent again and recorded as a new Sent entry.</summary>
    Sent,

    /// <summary>No entry has that identifier.</summary>
    NotFound,

    /// <summary>The entry has no recipient or no email content; nothing was sent or recorded.</summary>
    NothingToSend,

    /// <summary>The entry is a verification email whose link has expired; nothing was sent or recorded.</summary>
    LinkExpired,

    /// <summary>The booking is known to be cancelled, so its booking emails cannot be resent; nothing was sent or recorded.</summary>
    BookingCancelled,

    /// <summary>The trip has already departed; nothing was sent or recorded.</summary>
    TripDeparted,

    /// <summary>The recipient is on the do-not-send list; nothing was sent or recorded.</summary>
    AddressSuppressed,

    /// <summary>The email service refused the email for good; a new Failed entry was recorded.</summary>
    Refused,

    /// <summary>The email service could not be reached; a new Failed entry was recorded.</summary>
    EmailServiceUnavailable,
}

/// <summary>The outcome of a resend, with the new entry on success or the facts a refusal reports.</summary>
public sealed record ResendEmailResult
{
    /// <summary>How the resend ended.</summary>
    public required ResendEmailOutcome Outcome { get; init; }

    /// <summary>The new Sent entry, when the outcome is Sent.</summary>
    public ResendEmailResponse? Response { get; init; }

    /// <summary>The entry's own failure reason, when there is nothing to send.</summary>
    public FailureReason? FailureReason { get; init; }

    /// <summary>When the verification link expired, when the outcome is LinkExpired.</summary>
    public DateTimeOffset? LinkExpiredAt { get; init; }

    /// <summary>The cancelled booking, when the outcome is BookingCancelled.</summary>
    public Guid? BookingId { get; init; }

    /// <summary>When the trip departed, when the outcome is TripDeparted.</summary>
    public DateTimeOffset? DepartsAt { get; init; }

    /// <summary>The suppressed recipient, when the outcome is AddressSuppressed.</summary>
    public string? ToAddress { get; init; }

    /// <summary>Why the recipient is on the do-not-send list, when the outcome is AddressSuppressed.</summary>
    public SuppressionReason? SuppressionReason { get; init; }

    /// <summary>The new Failed entry, when the email service refused the email or could not be reached.</summary>
    public Guid? NewEntryId { get; init; }

    /// <summary>The email service's own reason, when it refused the email.</summary>
    public string? ProviderReason { get; init; }
}

/// <summary>Checks the entry identifier is one: a malformed identifier is a bad request, not an unknown entry.</summary>
public sealed class ResendEmailValidator : AbstractValidator<ResendEmailRequest>
{
    public ResendEmailValidator()
    {
        RuleFor(request => request.SendLogEntryId)
            .Must(IsEntryIdentifier)
            .WithMessage("'sendLogEntryId' must be a send log entry identifier (a UUID).")
            .OverridePropertyName("sendLogEntryId");
    }

    private static bool IsEntryIdentifier(string? sendLogEntryId)
    {
        return Guid.TryParse(sendLogEntryId, out Guid _);
    }
}

/// <summary>The resend operation's endpoint: validates the identifier and turns each outcome into its answer.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Notification")]
[Route("internal/sendLogEntries/{sendLogEntryId}/resend")]
public sealed class ResendEmailController : ControllerBase
{
    private readonly ResendEmailHandler _handler;
    private readonly ResendEmailValidator _validator = new();

    public ResendEmailController(EmailWorkerPorts ports, IConfiguration configuration, ILogger<ResendEmailHandler> logger)
    {
        _handler = new ResendEmailHandler(ports, configuration, logger);
    }

    /// <summary>Send the email of one send log entry again (internal).</summary>
    /// <param name="sendLogEntryId">The identifier of the entry to resend.</param>
    /// <param name="cancellationToken">Cancelled when the caller goes away.</param>
    /// <response code="200">The email was sent again. The body is the new entry, in status sent.</response>
    /// <response code="400">The entry identifier is malformed (RFC 7807 problem+json).</response>
    /// <response code="401">The internal key is missing or wrong (UNAUTHORIZED).</response>
    /// <response code="404">No entry has that identifier (NOT_FOUND).</response>
    /// <response code="422">The entry has nothing to send (RESEND_NOT_POSSIBLE), its verification link has expired (RESEND_LINK_EXPIRED), its booking is cancelled (RESEND_BOOKING_CANCELLED), its trip departed (RESEND_TRIP_DEPARTED), its recipient is on the do-not-send list (ADDRESS_SUPPRESSED), or the email service refused it (EMAIL_REFUSED).</response>
    /// <response code="502">The email service is unavailable; the attempt was recorded as Failed (UPSTREAM_UNAVAILABLE).</response>
    [Authorize]
    [HttpPost]
    [ProducesResponseType(typeof(ResendEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ResendEmailAsync(string sendLogEntryId, CancellationToken cancellationToken)
    {
        var request = new ResendEmailRequest(sendLogEntryId);
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        ResendEmailResult result = await _handler.HandleAsync(request, cancellationToken);
        switch (result.Outcome)
        {
            case ResendEmailOutcome.Sent:
                return Ok(result.Response);
            case ResendEmailOutcome.NotFound:
                return ProblemResult.NotFound(this, $"No send log entry exists with id '{sendLogEntryId}'.");
            case ResendEmailOutcome.NothingToSend:
                return NothingToSend(result.FailureReason!.Value);
            case ResendEmailOutcome.LinkExpired:
                return ProblemResult.ResendLinkExpired(
                    this, "Ask the customer to use Resend verification on the website.", result.LinkExpiredAt!.Value);
            case ResendEmailOutcome.BookingCancelled:
                return ProblemResult.ResendBookingCancelled(
                    this, "Emails about this booking can no longer be resent. The cancellation and refund emails can.",
                    result.BookingId!.Value);
            case ResendEmailOutcome.TripDeparted:
                return ProblemResult.ResendTripDeparted(
                    this, "Emails about a departed trip can no longer be resent.", result.DepartsAt!.Value);
            case ResendEmailOutcome.AddressSuppressed:
                return ProblemResult.AddressSuppressed(
                    this, "Remove the address from the do-not-send list first, then resend.",
                    result.ToAddress!, WireName.Of(result.SuppressionReason!.Value));
            case ResendEmailOutcome.Refused:
                return ProblemResult.EmailRefused(this, "The recipient address was refused by the email service.",
                    result.NewEntryId!.Value, result.ProviderReason!);
            case ResendEmailOutcome.EmailServiceUnavailable:
                return ProblemResult.UpstreamUnavailable(this,
                    "The email service is unavailable; the resend was recorded as failed. Try again later.",
                    true, result.NewEntryId!.Value);
            default:
                throw new InvalidOperationException($"Unhandled outcome '{result.Outcome}'.");
        }
    }

    private ProblemResult NothingToSend(FailureReason failureReason)
    {
        string detail = "This email cannot be resent: the event data was unusable.";
        if (failureReason == FailureReason.MissingRecipient)
        {
            detail = "This email cannot be resent: no recipient.";
        }

        return ProblemResult.ResendNotPossible(this, detail, WireName.Of(failureReason));
    }
}
