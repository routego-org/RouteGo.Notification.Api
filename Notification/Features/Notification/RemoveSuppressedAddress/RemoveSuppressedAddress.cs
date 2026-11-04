namespace Notification.Features.Notification.RemoveSuppressedAddress;

/// <summary>Which do-not-send entry to remove, exactly as the caller gave it in the route.</summary>
public sealed record RemoveSuppressedAddressRequest(string? SuppressedAddressId);

/// <summary>Checks the entry identifier is one: a malformed identifier is a bad request, not an unknown entry.</summary>
public sealed class RemoveSuppressedAddressValidator : AbstractValidator<RemoveSuppressedAddressRequest>
{
    public RemoveSuppressedAddressValidator()
    {
        RuleFor(request => request.SuppressedAddressId)
            .Must(IsEntryIdentifier)
            .WithMessage("'suppressedAddressId' must be a do-not-send entry identifier (a UUID).")
            .OverridePropertyName("suppressedAddressId");
    }

    private static bool IsEntryIdentifier(string? suppressedAddressId)
    {
        return Guid.TryParse(suppressedAddressId, out Guid _);
    }
}

/// <summary>
/// Takes one address off the do-not-send list at once, so emails to it are sent again. Nothing is
/// resent: support resends what the customer needs. A later bounce or complaint puts the address
/// back as a new entry.
/// </summary>
public sealed class RemoveSuppressedAddressHandler
{
    private const string WriteAction = "RemoveSuppressedAddress";

    private const string RemoveSql = """
        DELETE FROM notification.suppressed_addresses
        WHERE id = @Id;
        """;

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly ILogger<RemoveSuppressedAddressHandler> _logger;

    public RemoveSuppressedAddressHandler(IWriterConnectionFactory writerFactory, ILogger<RemoveSuppressedAddressHandler> logger)
    {
        _writerFactory = writerFactory;
        _logger = logger;
    }

    /// <summary>Removes the entry; false when no entry has that identifier, including one already removed.</summary>
    public async Task<bool> HandleAsync(RemoveSuppressedAddressRequest request, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(request.SuppressedAddressId!);

        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        int removed = await writer.Connection.ExecuteAsync(new CommandDefinition(
            RemoveSql, new { Id = id }, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
        if (removed == 0)
        {
            return false;
        }

        _logger.LogInformation("Do-not-send entry {SuppressedAddressId} removed", id);
        return true;
    }
}

/// <summary>The removal operation's endpoint: validates the identifier and answers 204, or 404 for an unknown entry.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Notification")]
[Route("internal/suppressedAddresses/{suppressedAddressId}")]
public sealed class RemoveSuppressedAddressController : ControllerBase
{
    private readonly RemoveSuppressedAddressHandler _handler;
    private readonly RemoveSuppressedAddressValidator _validator = new();

    public RemoveSuppressedAddressController(IWriterConnectionFactory writerFactory, ILogger<RemoveSuppressedAddressHandler> logger)
    {
        _handler = new RemoveSuppressedAddressHandler(writerFactory, logger);
    }

    /// <summary>Take one address off the do-not-send list (internal).</summary>
    /// <param name="suppressedAddressId">The identifier of the entry to remove.</param>
    /// <param name="cancellationToken">Cancelled when the caller goes away.</param>
    /// <response code="204">The address is no longer on the do-not-send list.</response>
    /// <response code="400">The entry identifier is malformed (RFC 7807 problem+json).</response>
    /// <response code="401">The internal key is missing or wrong (UNAUTHORIZED).</response>
    /// <response code="404">No entry has that identifier (NOT_FOUND).</response>
    [Authorize]
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveSuppressedAddressAsync(string suppressedAddressId, CancellationToken cancellationToken)
    {
        var request = new RemoveSuppressedAddressRequest(suppressedAddressId);
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        bool wasRemoved = await _handler.HandleAsync(request, cancellationToken);
        if (!wasRemoved)
        {
            return ProblemResult.NotFound(this, $"No do-not-send entry exists with id '{suppressedAddressId}'.");
        }

        return NoContent();
    }
}
