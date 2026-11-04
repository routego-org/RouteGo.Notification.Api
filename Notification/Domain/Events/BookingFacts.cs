namespace Notification.Domain;

/// <summary>
/// What Booking, Ticket Generator and Payment events say about a booking, read leniently: every
/// member is empty when the event lacks it, and each email slice requires only what its email needs.
/// </summary>
public sealed record BookingFacts
{
    public Guid? BookingId { get; init; }
    public string? ContactName { get; init; }
    public string? ContactEmail { get; init; }
    public string? OperatorName { get; init; }
    public string? OperatorEmail { get; init; }
    public string? Source { get; init; }
    public string? Destination { get; init; }
    public DateTimeOffset? DepartsAt { get; init; }
    public string? BusName { get; init; }
    public string? BusType { get; init; }
    public bool? IsAc { get; init; }
    public long? TotalAmount { get; init; }

    /// <summary>Every seat of the booking, in the order the event lists them.</summary>
    public IReadOnlyList<SeatFacts> Seats { get; init; } = new List<SeatFacts>();

    /// <summary>Reads the booking's facts from an event's data object.</summary>
    public static BookingFacts Read(JsonElement data)
    {
        JsonElement contact = EventData.Child(data, "contact");
        JsonElement operatorParty = EventData.Child(data, "operator");
        JsonElement trip = EventData.Child(data, "trip");
        return new BookingFacts
        {
            BookingId = EventData.Id(data, "bookingId"),
            ContactName = EventData.Text(contact, "name"),
            ContactEmail = EventData.Text(contact, "email"),
            OperatorName = EventData.Text(operatorParty, "name"),
            OperatorEmail = EventData.Text(operatorParty, "email"),
            Source = EventData.Text(trip, "source"),
            Destination = EventData.Text(trip, "destination"),
            DepartsAt = EventData.Time(trip, "departureUtc"),
            BusName = EventData.Text(trip, "busName"),
            BusType = EventData.Text(trip, "busType"),
            IsAc = EventData.Flag(trip, "isAc"),
            TotalAmount = EventData.Whole(data, "totalAmount"),
            Seats = ReadSeats(data),
        };
    }

    // Booking's spec lists a seat's passenger name, age and gender beside the seat; they are read
    // from the seat itself or from a nested passenger object, whichever the producer uses.
    private static List<SeatFacts> ReadSeats(JsonElement data)
    {
        var seats = new List<SeatFacts>();
        foreach (JsonElement seat in EventData.Items(data, "seats"))
        {
            JsonElement passenger = EventData.Child(seat, "passenger");
            string? name = EventData.Text(seat, "name");
            if (name is null)
            {
                name = EventData.Text(passenger, "name");
            }

            long? age = EventData.Whole(seat, "age");
            if (age is null)
            {
                age = EventData.Whole(passenger, "age");
            }

            string? gender = EventData.Text(seat, "gender");
            if (gender is null)
            {
                gender = EventData.Text(passenger, "gender");
            }

            seats.Add(new SeatFacts
            {
                Label = EventData.Text(seat, "seatNo"),
                PassengerName = name,
                Age = age,
                Gender = gender,
            });
        }

        return seats;
    }
}

/// <summary>One seat of a booking with its passenger.</summary>
public sealed record SeatFacts
{
    public string? Label { get; init; }
    public string? PassengerName { get; init; }
    public long? Age { get; init; }
    public string? Gender { get; init; }
}
