using System.Globalization;

namespace Notification.Domain;

/// <summary>How every RouteGo email writes a time and an amount of money.</summary>
public static class EmailFormats
{
    // India has no daylight saving, so a fixed offset is exact and needs no time zone database.
    private static readonly TimeSpan _istOffset = new TimeSpan(5, 30, 0);

    /// <summary>A moment in IST, written like <c>12 Nov 2026, 20:00 IST</c>.</summary>
    public static string Moment(DateTimeOffset moment)
    {
        DateTimeOffset local = moment.ToOffset(_istOffset);
        return local.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " IST";
    }

    /// <summary>The IST date of a moment alone, written like <c>12 Nov 2026</c>.</summary>
    public static string Date(DateTimeOffset moment)
    {
        DateTimeOffset local = moment.ToOffset(_istOffset);
        return local.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Whole rupees, written like <c>₹1,300</c>.</summary>
    public static string Rupees(long amount)
    {
        return "₹" + amount.ToString("N0", CultureInfo.InvariantCulture);
    }
}
