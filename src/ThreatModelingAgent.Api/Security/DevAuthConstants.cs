namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Constants for the local development auth mode (DevAuth:Enabled=true).
/// These values only appear in locally-signed JWTs that never leave the dev machine.
/// </summary>
public static class DevAuthConstants
{
    public const string Issuer = "dev";
    public const string Audience = "dev";
    public const int TokenLifetimeHours = 8;
}

/// <summary>
/// Holds the HMAC signing key for the DevLoginController.
/// Registered with a null key when DevAuth is disabled so DI resolves successfully;
/// the controller checks IsEnabled before using the key.
/// </summary>
public sealed class DevAuthSigningKeyHolder(string? key)
{
    public bool IsEnabled => key is not null;
    public string Key => key ?? throw new InvalidOperationException("DevAuth is not enabled.");
}
