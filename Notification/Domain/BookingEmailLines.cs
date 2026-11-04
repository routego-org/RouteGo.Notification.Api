namespace Notification.Domain;

/// <summary>The lines several booking emails share, written the way every one of them words them.</summary>
public static class BookingEmailLines
{
    /// <summary>One line per seat, <c>Seat 12 – Ravi Kumar</c>, in the order the event lists the seats.</summary>
    public static string[] SeatLines(IReadOnlyList<SeatFacts> seats)
    {
        return seats.Select(seat => $"Seat {seat.Label} – {seat.PassengerName}").ToArray();
    }

    /// <summary>One line per seat with the passenger's age and gender, <c>Seat 12 – Ravi Kumar, 34, Male</c>.</summary>
    public static string[] PassengerLines(IReadOnlyList<SeatFacts> seats)
    {
        return seats.Select(seat => $"Seat {seat.Label} – {seat.PassengerName}, {seat.Age}, {seat.Gender}").ToArray();
    }

    /// <summary>The trip's route line, <c>Trip: Mumbai to Pune</c>.</summary>
    public static string Route(string source, string destination)
    {
        return $"Trip: {source} to {destination}";
    }

    /// <summary>The departure line, <c>Departs: 12 Nov 2026, 20:00 IST</c>.</summary>
    public static string Departure(DateTimeOffset departsAt)
    {
        return $"Departs: {EmailFormats.Moment(departsAt)}";
    }

    /// <summary>Whether every seat names its label and passenger, and at least one seat exists.</summary>
    public static bool HasUsableSeats(IReadOnlyList<SeatFacts> seats)
    {
        if (seats.Count == 0)
        {
            return false;
        }

        return seats.All(seat => seat.Label != null && seat.PassengerName != null);
    }
}
