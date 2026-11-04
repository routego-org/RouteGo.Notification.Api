using System.Globalization;

namespace Notification.Domain;

/// <summary>
/// Reads fields out of an event's data without ever throwing: a missing, mistyped or blank field
/// reads as null (or as an undefined element for an object), so each email slice decides what a
/// missing field means for its email.
/// </summary>
public static class EventData
{
    /// <summary>A member of <paramref name="parent"/> that is itself an object or array; undefined when absent.</summary>
    public static JsonElement Child(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            return default;
        }

        return value;
    }

    /// <summary>A text member; null when absent, not text or blank.</summary>
    public static string? Text(JsonElement parent, string name)
    {
        JsonElement value = Child(parent, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text;
    }

    /// <summary>A whole-number member; null when absent or not a whole number.</summary>
    public static long? Whole(JsonElement parent, string name)
    {
        JsonElement value = Child(parent, name);
        if (value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        bool isWhole = value.TryGetInt64(out long number);
        if (!isWhole)
        {
            return null;
        }

        return number;
    }

    /// <summary>A true/false member; null when absent or not a boolean.</summary>
    public static bool? Flag(JsonElement parent, string name)
    {
        JsonElement value = Child(parent, name);
        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return null;
    }

    /// <summary>A date-time member as UTC; null when absent or not a date-time.</summary>
    public static DateTimeOffset? Time(JsonElement parent, string name)
    {
        string? text = Text(parent, name);
        if (text is null)
        {
            return null;
        }

        bool isTime = DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment);
        if (!isTime)
        {
            return null;
        }

        return moment.ToUniversalTime();
    }

    /// <summary>An identifier member; null when absent or not a UUID.</summary>
    public static Guid? Id(JsonElement parent, string name)
    {
        string? text = Text(parent, name);
        if (text is null)
        {
            return null;
        }

        bool isId = Guid.TryParse(text, out Guid id);
        if (!isId)
        {
            return null;
        }

        return id;
    }

    /// <summary>The elements of an array member; empty when absent or not an array.</summary>
    public static IReadOnlyList<JsonElement> Items(JsonElement parent, string name)
    {
        JsonElement value = Child(parent, name);
        var items = new List<JsonElement>();
        if (value.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (JsonElement item in value.EnumerateArray())
        {
            items.Add(item);
        }

        return items;
    }
}
