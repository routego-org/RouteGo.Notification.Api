namespace Notification.Features.Notification.SearchSendLog;

/// <summary>Which send log entries to list: every email attempted to one address, a page at a time.</summary>
public sealed record SearchSendLogRequest
{
    /// <summary>The recipient email address to search for. Matched exactly, case-insensitively.</summary>
    [FromQuery(Name = "toAddress")]
    public string? ToAddress { get; init; }

    /// <summary>1-based page number; the first page when omitted.</summary>
    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    /// <summary>Entries per page, 1 to 200; 20 when omitted.</summary>
    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

/// <summary>One page of the entries to the address, newest attempt first, with the total across all pages.</summary>
public sealed record SearchSendLogResponse
{
    /// <summary>The entries on this page, newest attempt first. Empty when nothing matched.</summary>
    public required IReadOnlyList<SearchSendLogEntry> Items { get; init; }

    /// <summary>How many entries to the address exist across every page.</summary>
    public required int TotalCount { get; init; }

    /// <summary>The page returned.</summary>
    public required int Page { get; init; }

    /// <summary>The page size used.</summary>
    public required int PageSize { get; init; }
}

/// <summary>One email attempt as support sees it: everything recorded about it except the message itself.</summary>
public sealed record SearchSendLogEntry
{
    /// <summary>The entry's identifier, used to resend it.</summary>
    public required Guid SendLogEntryId { get; init; }

    /// <summary>The event ID from the producing service's envelope. A resend keeps the original's value.</summary>
    public required string EventId { get; init; }

    /// <summary>The event that caused the email.</summary>
    public required EventType EventType { get; init; }

    /// <summary>Which email this is.</summary>
    public required EmailType EmailType { get; init; }

    /// <summary>The booking the email is about; null for the Identity emails.</summary>
    public Guid? BookingId { get; init; }

    /// <summary>The trip's departure time (UTC) for an email about a trip, when known; otherwise null.</summary>
    public DateTimeOffset? DepartsAt { get; init; }

    /// <summary>The recipient as given in the event; null only when the event had no recipient.</summary>
    public string? ToAddress { get; init; }

    /// <summary>The subject exactly as sent; null when the email could not be built.</summary>
    public string? Subject { get; init; }

    /// <summary>Where the attempt stands.</summary>
    public required SendStatus Status { get; init; }

    /// <summary>Why the attempt failed; present exactly when the status is failed.</summary>
    public FailureReason? FailureReason { get; init; }

    /// <summary>Why the attempt was skipped; present exactly when the status is skipped.</summary>
    public SkipReason? SkipReason { get; init; }

    /// <summary>Detail of the last problem: the missing field, the email service's reason, or the last temporary error.</summary>
    public string? FailureDetail { get; init; }

    /// <summary>The email service's receipt number; present exactly when the status is sent.</summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>The entry this entry resends; null for an email sent from an event.</summary>
    public Guid? ResendOf { get; init; }

    /// <summary>When the event occurred (UTC). For verification emails, the start of the link's lifetime.</summary>
    public required DateTimeOffset EventOccurredAt { get; init; }

    /// <summary>The event's correlation ID.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>When this attempt was recorded (UTC).</summary>
    public required DateTimeOffset AttemptedAt { get; init; }

    /// <summary>When the email service accepted the email (UTC); present exactly when the status is sent.</summary>
    public DateTimeOffset? SentAt { get; init; }
}

/// <summary>Checks the address is one and the paging is in range.</summary>
public sealed class SearchSendLogValidator : AbstractValidator<SearchSendLogRequest>
{
    // The longest address the email standards allow, and the contract's limit.
    private const int MaxAddressLength = 254;

    public SearchSendLogValidator()
    {
        RuleFor(request => request.ToAddress)
            .NotEmpty()
            .MaximumLength(MaxAddressLength)
            .EmailAddress()
            .OverridePropertyName("toAddress");
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(SearchSendLogHandler.FirstPage)
            .OverridePropertyName("page");
        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, SearchSendLogHandler.MaxPageSize)
            .OverridePropertyName("pageSize");
    }
}

/// <summary>
/// Lists the send log entries to one address, compared case-insensitively, newest attempt first, a
/// page at a time. Entries with no recipient have no address to match, so they are never listed.
/// </summary>
public sealed class SearchSendLogHandler
{
    /// <summary>The page returned when none is asked for.</summary>
    public const int FirstPage = 1;

    /// <summary>The page size used when none is asked for.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page size a caller may ask for.</summary>
    public const int MaxPageSize = 200;

    private const string CountSql = """
        SELECT count(*)::int
        FROM notification.send_log_entries
        WHERE lower(to_address) = lower(@ToAddress);
        """;

    // id breaks ties between attempts recorded at the same moment, so paging never repeats or skips one.
    private const string PageSql = """
        SELECT id, event_id, event_type, email_type, booking_id, departs_at, to_address, subject, status,
               failure_reason, skip_reason, failure_detail, provider_message_id, resend_of, event_occurred_at, correlation_id,
               attempted_at, sent_at
        FROM notification.send_log_entries
        WHERE lower(to_address) = lower(@ToAddress)
        ORDER BY attempted_at DESC, id DESC
        LIMIT @PageSize OFFSET @Offset;
        """;

    private readonly IReaderConnectionFactory _readerFactory;

    public SearchSendLogHandler(IReaderConnectionFactory readerFactory)
    {
        _readerFactory = readerFactory;
    }

    /// <summary>Returns the requested page of entries to the address, with the total count.</summary>
    public async Task<SearchSendLogResponse> HandleAsync(SearchSendLogRequest request, CancellationToken cancellationToken)
    {
        int page = request.Page ?? FirstPage;
        int pageSize = request.PageSize ?? DefaultPageSize;
        long offset = (long)(page - 1) * pageSize;
        var parameters = new { request.ToAddress, PageSize = pageSize, Offset = offset };

        await using DbConnection connection = _readerFactory.CreateReadConnection();
        await connection.OpenAsync(cancellationToken);
        int totalCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            CountSql, parameters, cancellationToken: cancellationToken));
        IEnumerable<EntryRow> rows = await connection.QueryAsync<EntryRow>(new CommandDefinition(
            PageSql, parameters, cancellationToken: cancellationToken));

        return Map(rows, totalCount, page, pageSize);
    }

    private static SearchSendLogResponse Map(IEnumerable<EntryRow> rows, int totalCount, int page, int pageSize)
    {
        var items = new List<SearchSendLogEntry>();
        foreach (EntryRow row in rows)
        {
            items.Add(MapEntry(row));
        }

        return new SearchSendLogResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static SearchSendLogEntry MapEntry(EntryRow row)
    {
        DateTimeOffset? sentAt = null;
        if (row.SentAt != null)
        {
            sentAt = AsUtc(row.SentAt.Value);
        }

        DateTimeOffset? departsAt = null;
        if (row.DepartsAt != null)
        {
            departsAt = AsUtc(row.DepartsAt.Value);
        }

        return new SearchSendLogEntry
        {
            SendLogEntryId = row.Id,
            EventId = row.EventId,
            EventType = row.EventType,
            EmailType = row.EmailType,
            BookingId = row.BookingId,
            DepartsAt = departsAt,
            ToAddress = row.ToAddress,
            Subject = row.Subject,
            Status = row.Status,
            FailureReason = row.FailureReason,
            SkipReason = row.SkipReason,
            FailureDetail = row.FailureDetail,
            ProviderMessageId = row.ProviderMessageId,
            ResendOf = row.ResendOf,
            EventOccurredAt = AsUtc(row.EventOccurredAt),
            CorrelationId = row.CorrelationId,
            AttemptedAt = AsUtc(row.AttemptedAt),
            SentAt = sentAt,
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
        public string EventId { get; init; } = string.Empty;
        public EventType EventType { get; init; }
        public EmailType EmailType { get; init; }
        public Guid? BookingId { get; init; }
        public DateTime? DepartsAt { get; init; }
        public string? ToAddress { get; init; }
        public string? Subject { get; init; }
        public SendStatus Status { get; init; }
        public FailureReason? FailureReason { get; init; }
        public SkipReason? SkipReason { get; init; }
        public string? FailureDetail { get; init; }
        public string? ProviderMessageId { get; init; }
        public Guid? ResendOf { get; init; }
        public DateTime EventOccurredAt { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
        public DateTime AttemptedAt { get; init; }
        public DateTime? SentAt { get; init; }
    }
}

/// <summary>The send log search's endpoint: validates the request and returns the page.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Notification")]
[Route("internal/sendLogEntries")]
public sealed class SearchSendLogController : ControllerBase
{
    private readonly SearchSendLogHandler _handler;
    private readonly SearchSendLogValidator _validator = new();

    public SearchSendLogController(IReaderConnectionFactory readerFactory)
    {
        _handler = new SearchSendLogHandler(readerFactory);
    }

    /// <summary>List every email attempted to one address, newest first (internal).</summary>
    /// <response code="200">The requested page of entries, newest first. No match is an empty page, not an error.</response>
    /// <response code="400">The address is missing or malformed, or the paging is out of range (RFC 7807 problem+json).</response>
    /// <response code="401">The internal key is missing or wrong (UNAUTHORIZED).</response>
    [Authorize]
    [HttpGet]
    [ProducesResponseType(typeof(SearchSendLogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SearchSendLogAsync([FromQuery] SearchSendLogRequest request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        SearchSendLogResponse response = await _handler.HandleAsync(request, cancellationToken);
        return Ok(response);
    }
}
