namespace Notification.Features.Notification.ListSuppressedAddresses;

/// <summary>Which do-not-send entries to list: the whole list, or one address, a page at a time.</summary>
public sealed record ListSuppressedAddressesRequest
{
    /// <summary>Return only the entry for this address. Matched exactly, case-insensitively.</summary>
    [FromQuery(Name = "emailAddress")]
    public string? EmailAddress { get; init; }

    /// <summary>1-based page number; the first page when omitted.</summary>
    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    /// <summary>Entries per page, 1 to 200; 20 when omitted.</summary>
    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

/// <summary>One page of the do-not-send list, newest first, with the total across all pages.</summary>
public sealed record ListSuppressedAddressesResponse
{
    /// <summary>The entries on this page, newest first. Empty when nothing matched.</summary>
    public required IReadOnlyList<SuppressedAddressEntry> Items { get; init; }

    /// <summary>How many entries match across every page.</summary>
    public required int TotalCount { get; init; }

    /// <summary>The page returned.</summary>
    public required int Page { get; init; }

    /// <summary>The page size used.</summary>
    public required int PageSize { get; init; }
}

/// <summary>One address on the do-not-send list, with why it is there.</summary>
public sealed record SuppressedAddressEntry
{
    /// <summary>The entry's identifier, used to remove it.</summary>
    public required Guid SuppressedAddressId { get; init; }

    /// <summary>The address that is not emailed.</summary>
    public required string EmailAddress { get; init; }

    /// <summary>Why it was added.</summary>
    public required SuppressionReason Reason { get; init; }

    /// <summary>The email service's receipt number of the email that bounced or was reported, when the report named it.</summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>The email service's own description of the bounce or complaint.</summary>
    public string? ReportDetail { get; init; }

    /// <summary>When the email service says the bounce or complaint happened (UTC).</summary>
    public required DateTimeOffset ReportedAt { get; init; }

    /// <summary>When the address was put on the list (UTC).</summary>
    public required DateTimeOffset SuppressedAt { get; init; }
}

/// <summary>Checks the optional address is one and the paging is in range.</summary>
public sealed class ListSuppressedAddressesValidator : AbstractValidator<ListSuppressedAddressesRequest>
{
    // The longest address the email standards allow, and the contract's limit.
    private const int MaxAddressLength = 254;

    public ListSuppressedAddressesValidator()
    {
        RuleFor(request => request.EmailAddress)
            .MaximumLength(MaxAddressLength)
            .EmailAddress()
            .When(request => request.EmailAddress != null)
            .OverridePropertyName("emailAddress");
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(ListSuppressedAddressesHandler.FirstPage)
            .OverridePropertyName("page");
        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, ListSuppressedAddressesHandler.MaxPageSize)
            .OverridePropertyName("pageSize");
    }
}

/// <summary>
/// Lists the do-not-send list newest first, a page at a time, optionally for one address compared
/// case-insensitively. No match is an empty page, not an error.
/// </summary>
public sealed class ListSuppressedAddressesHandler
{
    /// <summary>The page returned when none is asked for.</summary>
    public const int FirstPage = 1;

    /// <summary>The page size used when none is asked for.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page size a caller may ask for.</summary>
    public const int MaxPageSize = 200;

    private const string CountSql = """
        SELECT count(*)::int
        FROM notification.suppressed_addresses
        WHERE @EmailAddress::text IS NULL OR lower(email_address) = lower(@EmailAddress::text);
        """;

    // id breaks ties between entries added at the same moment, so paging never repeats or skips one.
    private const string PageSql = """
        SELECT id, email_address, reason, provider_message_id, report_detail, reported_at, suppressed_at
        FROM notification.suppressed_addresses
        WHERE @EmailAddress::text IS NULL OR lower(email_address) = lower(@EmailAddress::text)
        ORDER BY suppressed_at DESC, id DESC
        LIMIT @PageSize OFFSET @Offset;
        """;

    private readonly IReaderConnectionFactory _readerFactory;

    public ListSuppressedAddressesHandler(IReaderConnectionFactory readerFactory)
    {
        _readerFactory = readerFactory;
    }

    /// <summary>Returns the requested page of entries, with the total count.</summary>
    public async Task<ListSuppressedAddressesResponse> HandleAsync(
        ListSuppressedAddressesRequest request, CancellationToken cancellationToken)
    {
        int page = request.Page ?? FirstPage;
        int pageSize = request.PageSize ?? DefaultPageSize;
        long offset = (long)(page - 1) * pageSize;
        var parameters = new { request.EmailAddress, PageSize = pageSize, Offset = offset };

        await using DbConnection connection = _readerFactory.CreateReadConnection();
        await connection.OpenAsync(cancellationToken);
        int totalCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            CountSql, parameters, cancellationToken: cancellationToken));
        IEnumerable<EntryRow> rows = await connection.QueryAsync<EntryRow>(new CommandDefinition(
            PageSql, parameters, cancellationToken: cancellationToken));

        return Map(rows, totalCount, page, pageSize);
    }

    private static ListSuppressedAddressesResponse Map(IEnumerable<EntryRow> rows, int totalCount, int page, int pageSize)
    {
        var items = new List<SuppressedAddressEntry>();
        foreach (EntryRow row in rows)
        {
            items.Add(MapEntry(row));
        }

        return new ListSuppressedAddressesResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static SuppressedAddressEntry MapEntry(EntryRow row)
    {
        return new SuppressedAddressEntry
        {
            SuppressedAddressId = row.Id,
            EmailAddress = row.EmailAddress,
            Reason = row.Reason,
            ProviderMessageId = row.ProviderMessageId,
            ReportDetail = row.ReportDetail,
            ReportedAt = AsUtc(row.ReportedAt),
            SuppressedAt = AsUtc(row.SuppressedAt),
        };
    }

    // The driver reads timestamptz as a UTC DateTime; the contract's times carry their offset.
    private static DateTimeOffset AsUtc(DateTime utcTime)
    {
        return new DateTimeOffset(utcTime, TimeSpan.Zero);
    }

    private sealed record EntryRow
    {
        public Guid Id { get; init; }
        public string EmailAddress { get; init; } = string.Empty;
        public SuppressionReason Reason { get; init; }
        public string? ProviderMessageId { get; init; }
        public string? ReportDetail { get; init; }
        public DateTime ReportedAt { get; init; }
        public DateTime SuppressedAt { get; init; }
    }
}

/// <summary>The do-not-send list's endpoint: validates the request and returns the page.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Notification")]
[Route("internal/suppressedAddresses")]
public sealed class ListSuppressedAddressesController : ControllerBase
{
    private readonly ListSuppressedAddressesHandler _handler;
    private readonly ListSuppressedAddressesValidator _validator = new();

    public ListSuppressedAddressesController(IReaderConnectionFactory readerFactory)
    {
        _handler = new ListSuppressedAddressesHandler(readerFactory);
    }

    /// <summary>List the do-not-send list, newest first, optionally for one address (internal).</summary>
    /// <response code="200">The requested page of entries, newest first. No match is an empty page, not an error.</response>
    /// <response code="400">The address is malformed, or the paging is out of range (RFC 7807 problem+json).</response>
    /// <response code="401">The internal key is missing or wrong (UNAUTHORIZED).</response>
    [Authorize]
    [HttpGet]
    [ProducesResponseType(typeof(ListSuppressedAddressesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListSuppressedAddressesAsync(
        [FromQuery] ListSuppressedAddressesRequest request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        ListSuppressedAddressesResponse response = await _handler.HandleAsync(request, cancellationToken);
        return Ok(response);
    }
}
