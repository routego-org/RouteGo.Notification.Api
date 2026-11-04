namespace Notification.Domain;

/// <summary>
/// The Web Portal pages emails link to, each the configured base address joined with the page's
/// path. A base address that carries a path keeps it, because paths are joined relative to it.
/// </summary>
public sealed class PortalLinks
{
    private const string TicketPagePath = "tickets/";
    private const string OperatorTripsPath = "operator/trips";

    private readonly Uri _baseAddress;

    public PortalLinks(Uri baseAddress, Uri signInPage)
    {
        _baseAddress = baseAddress;
        SignInPage = signInPage;
    }

    /// <summary>The Portal's sign-in page.</summary>
    public Uri SignInPage { get; }

    /// <summary>The operator's trips page.</summary>
    public Uri OperatorTrips
    {
        get { return new Uri(_baseAddress, OperatorTripsPath); }
    }

    /// <summary>The ticket page of one booking.</summary>
    public Uri TicketPage(Guid bookingId)
    {
        return new Uri(_baseAddress, TicketPagePath + bookingId.ToString("D"));
    }
}
