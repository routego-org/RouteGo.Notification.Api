namespace Notification.Domain;

/// <summary>
/// Collects what an email needs from its event and reports the first thing missing, by field name
/// only — never by value, since the values are personal data.
/// </summary>
public sealed class FieldCheck
{
    private string? _firstMissing;

    /// <summary>The first missing field, written like <c>seats is missing</c>; null when nothing is missing.</summary>
    public string? Problem
    {
        get { return _firstMissing; }
    }

    /// <summary>Notes <paramref name="name"/> as missing unless <paramref name="isPresent"/>.</summary>
    public void Require(string name, bool isPresent)
    {
        if (!isPresent && _firstMissing is null)
        {
            _firstMissing = $"{name} is missing";
        }
    }
}
