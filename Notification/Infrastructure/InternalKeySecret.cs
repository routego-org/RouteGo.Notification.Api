namespace Notification.Infrastructure;

/// <summary>
/// Names the secret that holds the RouteGo internal key — the shared key RouteGo's own services
/// present when they call each other's internal operations (in this service, the Web Portal's
/// admin screens searching the send log and resending an email). One name in every environment:
/// Development reads it from user-secrets, every other environment from Secrets Manager. The
/// value is the key itself, as plain text.
/// </summary>
public static class InternalKeySecret
{
    /// <summary>
    /// The only lookup key, everywhere. Not under routego/notification/ because the key is shared:
    /// the Portal reads the same secret to call these operations.
    /// </summary>
    public const string SecretId = "routego/internal-key";
}
