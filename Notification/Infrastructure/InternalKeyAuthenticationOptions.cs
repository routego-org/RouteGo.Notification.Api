using Microsoft.AspNetCore.Authentication;

namespace Notification.Infrastructure;

/// <summary>Settings of the internal-key authentication scheme: the key every call must present.</summary>
public sealed class InternalKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The name the scheme is registered under, and the default scheme of this service.</summary>
    public const string SchemeName = "InternalKey";

    /// <summary>The request header that carries the key, as the contract's security scheme names it.</summary>
    public const string HeaderName = "X-Internal-Key";

    /// <summary>The expected key, resolved from the secret store at startup.</summary>
    public string Key { get; set; } = string.Empty;
}
