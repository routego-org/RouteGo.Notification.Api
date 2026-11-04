namespace Notification.Common.Problems;

/// <summary>
/// An RFC 7807 problem response reporting one cataloged problem type. Every business refusal
/// in every slice is built here, so the same refusal always carries the same type URI, title,
/// status, and machine-readable code — and the catalog row stays inspectable, which is what
/// lets the codebase be checked against the product's problem-type catalog rather than
/// trusted to match it.
/// <para>
/// Extending this: one factory method per row in the product's problem-type catalog, and
/// never a method for a code the catalog does not define. The <c>detail</c> sentence is
/// always a parameter — it is the one part the catalog leaves to the operation — and no
/// slice-specific logic belongs here. The constructor is private so a problem response can
/// only ever be created through a cataloged factory.
/// </para>
/// </summary>
public sealed class ProblemResult : ObjectResult
{
    private readonly ProblemDetails _problemDetails;

    private ProblemResult(ProblemDetails problemDetails, ProblemType problemType)
        : base(problemDetails)
    {
        _problemDetails = problemDetails;
        ProblemType = problemType;
        StatusCode = problemDetails.Status;
    }

    /// <summary>The catalog row this response reports. Clients switch on its code.</summary>
    public ProblemType ProblemType { get; }

    /// <summary>404 — the resource does not exist.</summary>
    public static ProblemResult NotFound(ControllerBase controller, string detail)
    {
        return Build(controller, ProblemTypes.NotFound, "The requested resource does not exist.",
            StatusCodes.Status404NotFound, detail);
    }

    /// <summary>409 — the request conflicts with existing state.</summary>
    public static ProblemResult Conflict(ControllerBase controller, string detail)
    {
        return Build(controller, ProblemTypes.Conflict, "The request conflicts with existing state.",
            StatusCodes.Status409Conflict, detail);
    }

    /// <summary>422 — a business rule refused an otherwise well-formed request.</summary>
    public static ProblemResult BusinessRuleViolated(ControllerBase controller, string detail)
    {
        return Build(controller, ProblemTypes.BusinessRuleViolated, "A business rule refused the request.",
            StatusCodes.Status422UnprocessableEntity, detail);
    }

    /// <summary>
    /// 502 — a required dependency is down or returned an unusable response, leaving no usable
    /// data. A fan-out that got data from at least one source is a SUCCESS: return 200 with the
    /// contract's partial-success fields instead of a problem.
    /// </summary>
    public static ProblemResult UpstreamUnavailable(ControllerBase controller, string detail, bool retryable)
    {
        ProblemResult result = Build(controller, ProblemTypes.UpstreamUnavailable,
            "A required upstream service is unavailable.",
            StatusCodes.Status502BadGateway, detail);
        result.AddExtension("retryable", retryable);
        return result;
    }

    /// <summary>
    /// 502 — the email service was unavailable while resending; <paramref name="sendLogEntryId"/>
    /// names the new Failed entry recorded for the attempt, so support can find it in the send log.
    /// </summary>
    public static ProblemResult UpstreamUnavailable(
        ControllerBase controller, string detail, bool retryable, Guid sendLogEntryId)
    {
        ProblemResult result = UpstreamUnavailable(controller, detail, retryable);
        result.AddExtension("sendLogEntryId", sendLogEntryId);
        return result;
    }

    /// <summary>
    /// 422 — the entry has no recipient or no email content; <paramref name="failureReason"/> is the
    /// entry's own failure reason in its wire spelling, which tells the caller which one.
    /// </summary>
    public static ProblemResult ResendNotPossible(ControllerBase controller, string detail, string failureReason)
    {
        ProblemResult result = Build(controller, ProblemTypes.ResendNotPossible, "This email cannot be resent.",
            StatusCodes.Status422UnprocessableEntity, detail);
        result.AddExtension("failureReason", failureReason);
        return result;
    }

    /// <summary>422 — the verification link in the email expired at <paramref name="linkExpiredAt"/>.</summary>
    public static ProblemResult ResendLinkExpired(ControllerBase controller, string detail, DateTimeOffset linkExpiredAt)
    {
        ProblemResult result = Build(controller, ProblemTypes.ResendLinkExpired,
            "The verification link in this email has expired.",
            StatusCodes.Status422UnprocessableEntity, detail);
        result.AddExtension("linkExpiredAt", linkExpiredAt);
        return result;
    }

    /// <summary>
    /// 422 — the email service refused the resent email for good. <paramref name="sendLogEntryId"/>
    /// names the new Failed entry; <paramref name="providerReason"/> is the email service's own reason.
    /// </summary>
    public static ProblemResult EmailRefused(
        ControllerBase controller, string detail, Guid sendLogEntryId, string providerReason)
    {
        ProblemResult result = Build(controller, ProblemTypes.EmailRefused, "The email service refused this email.",
            StatusCodes.Status422UnprocessableEntity, detail);
        result.AddExtension("sendLogEntryId", sendLogEntryId);
        result.AddExtension("providerReason", providerReason);
        return result;
    }

    /// <summary>422 — the booking of the email is known to be cancelled, so its booking emails cannot be resent.</summary>
    public static ProblemResult ResendBookingCancelled(ControllerBase controller, string detail, Guid bookingId)
    {
        ProblemResult result = Build(controller, ProblemTypes.ResendBookingCancelled, "This booking has been cancelled.",
            StatusCodes.Status422UnprocessableEntity, detail);
        result.AddExtension("bookingId", bookingId);
        return result;
    }

    /// <summary>422 — the trip of the email departed at <paramref name="departsAt"/>, so the email cannot be resent.</summary>
    public static ProblemResult ResendTripDeparted(ControllerBase controller, string detail, DateTimeOffset departsAt)
    {
        ProblemResult result = Build(controller, ProblemTypes.ResendTripDeparted, "This trip has already departed.",
            StatusCodes.Status422UnprocessableEntity, detail);
        result.AddExtension("departsAt", departsAt);
        return result;
    }

    /// <summary>
    /// 422 — the recipient is on the do-not-send list; <paramref name="suppressionReason"/> is why, in
    /// its wire spelling.
    /// </summary>
    public static ProblemResult AddressSuppressed(
        ControllerBase controller, string detail, string toAddress, string suppressionReason)
    {
        ProblemResult result = Build(controller, ProblemTypes.AddressSuppressed, "This address is on the do-not-send list.",
            StatusCodes.Status422UnprocessableEntity, detail);
        result.AddExtension("toAddress", toAddress);
        result.AddExtension("suppressionReason", suppressionReason);
        return result;
    }

    /// <summary>
    /// 504 — a required dependency did not respond in time and no data could be served. Pass
    /// retryAfterSeconds when the delay is known; the catalog documents it as a member clients
    /// act on. Same partial-success rule as UpstreamUnavailable.
    /// </summary>
    public static ProblemResult UpstreamTimeout(
        ControllerBase controller, string detail, bool retryable, int? retryAfterSeconds)
    {
        ProblemResult result = Build(controller, ProblemTypes.UpstreamTimeout,
            "A required upstream service did not respond in time.",
            StatusCodes.Status504GatewayTimeout, detail);
        result.AddExtension("retryable", retryable);
        if (retryAfterSeconds.HasValue)
        {
            result.AddExtension("retryAfterSeconds", retryAfterSeconds.Value);
        }

        return result;
    }

    /// <summary>
    /// Adds one extension member. Every member added here must appear in the catalog row's
    /// Extensions column — a member no consumer was told about is a member no consumer reads.
    /// </summary>
    private void AddExtension(string name, object value)
    {
        _problemDetails.Extensions[name] = value;
    }

    private static ProblemResult Build(
        ControllerBase controller, ProblemType problemType, string title, int statusCode, string detail)
    {
        // Built through the controller's ProblemDetailsFactory rather than by hand, because
        // that factory is what applies the host's customization — the traceId correlating this
        // response with server-side telemetry. A hand-constructed ProblemDetails loses it
        // silently, and the loss only shows up when someone tries to trace a reported error.
        ProblemDetails problemDetails = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode: statusCode,
            title: title,
            type: problemType.Type,
            detail: detail);
        problemDetails.Extensions["code"] = problemType.Code;
        return new ProblemResult(problemDetails, problemType);
    }
}
