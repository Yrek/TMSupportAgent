namespace ThreatModelingAgent.Domain.Interfaces;

/// <summary>
/// Abstraction over the WorkOS Management API for user and org operations.
/// Kept in Domain so callers (controllers, services) depend only on the interface.
///
/// SECURITY:
/// - API key is loaded from configuration at startup, never hardcoded.
/// - Invitation emails are sent by WorkOS directly — we never construct or send auth emails.
/// - WorkOS validates the invited email; we do not trust the result as proof of user identity.
/// </summary>
public interface IWorkOsClient
{
    /// <summary>
    /// Sends an invitation email via WorkOS for the given org.
    /// Returns the WorkOS invitation ID on success.
    /// Throws <see cref="WorkOsException"/> on API errors.
    /// </summary>
    Task<string> SendInvitationAsync(
        string email,
        string workOsOrgId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a WorkOS organization and returns its ID (e.g. "org_01XXXXX").
    /// Called when a new app org is created so the two are linked.
    /// Throws <see cref="WorkOsException"/> on API errors.
    /// </summary>
    Task<string> CreateOrganizationAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Deletes a WorkOS user account as part of GDPR erasure.
    /// Throws <see cref="WorkOsException"/> on API errors.
    /// </summary>
    Task DeleteUserAsync(string workOsUserId, CancellationToken ct = default);
}

/// <summary>Represents an error returned by the WorkOS Management API.</summary>
public sealed class WorkOsException(string message, int? statusCode = null)
    : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}
