namespace Notification.Tests.Support;

/// <summary>
/// The data of a Booking, Ticket Generator or Payment event, as those producers publish it. Defaults
/// to booking <see cref="DefaultId"/> for Ravi Kumar on BusCo's Mumbai to Pune bus; a test changes
/// only what its criterion is about, with <c>with</c>, and a null member is left out of the event.
/// </summary>
public sealed record TestBooking
{
    /// <summary>The booking every test event is about, unless a test says otherwise.</summary>
    public static readonly Guid DefaultId = Guid.Parse("3f6c2a10-7b4e-4d21-9a8f-5e0c1d2b3a44");

    /// <summary>The trip every test event departs on: 12 Nov 2026 14:30 UTC, which is 20:00 IST.</summary>
    public static readonly DateTimeOffset DefaultDeparture = new DateTimeOffset(2026, 11, 12, 14, 30, 0, TimeSpan.Zero);

    public Guid? BookingId { get; init; } = DefaultId;
    public string? ContactName { get; init; } = "Ravi Kumar";
    public string? ContactEmail { get; init; } = "ravi@gmail.com";
    public string? OperatorName { get; init; } = "BusCo Travels";
    public string? OperatorEmail { get; init; } = "bookings@busco.in";
    public string? Source { get; init; } = "Mumbai";
    public string? Destination { get; init; } = "Pune";
    public DateTimeOffset? DepartureUtc { get; init; } = DefaultDeparture;
    public string? BusName { get; init; } = "BusCo Volvo";
    public string? BusType { get; init; } = "Sleeper";
    public bool? IsAc { get; init; } = true;
    public long? TotalAmount { get; init; } = 1300;
    public IReadOnlyList<TestSeat> Seats { get; init; } = DefaultSeats();

    /// <summary>The event data object, with <paramref name="extras"/> added beside the booking's own members.</summary>
    public Dictionary<string, object?> Data(IReadOnlyDictionary<string, object?>? extras = null)
    {
        var data = new Dictionary<string, object?>();
        AddIfPresent(data, "bookingId", BookingId);
        AddIfPresent(data, "contact", Party(ContactName, ContactEmail));
        AddIfPresent(data, "operator", Party(OperatorName, OperatorEmail));
        AddIfPresent(data, "trip", Trip());
        AddIfPresent(data, "seats", SeatData());
        AddIfPresent(data, "totalAmount", TotalAmount);
        if (extras != null)
        {
            foreach (KeyValuePair<string, object?> extra in extras)
            {
                data[extra.Key] = extra.Value;
            }
        }

        return data;
    }

    private static List<TestSeat> DefaultSeats()
    {
        return new List<TestSeat>
        {
            new TestSeat("12", "Ravi Kumar", 34, "Male"),
            new TestSeat("13", "Asha Kumar", 31, "Female"),
        };
    }

    private static void AddIfPresent(Dictionary<string, object?> data, string name, object? value)
    {
        if (value != null)
        {
            data[name] = value;
        }
    }

    private static Dictionary<string, object?>? Party(string? name, string? email)
    {
        var party = new Dictionary<string, object?>();
        AddIfPresent(party, "name", name);
        AddIfPresent(party, "email", email);
        if (party.Count == 0)
        {
            return null;
        }

        return party;
    }

    private Dictionary<string, object?>? Trip()
    {
        var trip = new Dictionary<string, object?>();
        AddIfPresent(trip, "source", Source);
        AddIfPresent(trip, "destination", Destination);
        AddIfPresent(trip, "departureUtc", DepartureUtc);
        AddIfPresent(trip, "busName", BusName);
        AddIfPresent(trip, "busType", BusType);
        AddIfPresent(trip, "isAc", IsAc);
        if (trip.Count == 0)
        {
            return null;
        }

        return trip;
    }

    private List<Dictionary<string, object?>>? SeatData()
    {
        if (Seats.Count == 0)
        {
            return null;
        }

        var seats = new List<Dictionary<string, object?>>();
        foreach (TestSeat seat in Seats)
        {
            seats.Add(new Dictionary<string, object?>
            {
                ["seatNo"] = seat.Label,
                ["name"] = seat.PassengerName,
                ["age"] = seat.Age,
                ["gender"] = seat.Gender,
            });
        }

        return seats;
    }
}

/// <summary>One seat of a test booking and its passenger.</summary>
public sealed record TestSeat(string Label, string PassengerName, int Age, string Gender);
