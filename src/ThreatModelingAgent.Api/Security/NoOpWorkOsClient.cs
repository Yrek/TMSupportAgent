using ThreatModelingAgent.Domain.Interfaces;

namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// No-op WorkOS client used when DevAuth:Enabled=true.
/// WorkOS API calls (invitations, org creation, GDPR erasure) are unavailable in dev auth mode.
/// This prevents startup failure and DI activation errors when WorkOS:ApiKey is not configured.
/// </summary>
internal sealed class NoOpWorkOsClient : IWorkOsClient
{
    public Task<string> SendInvitationAsync(string email, string workOsOrgId, CancellationToken ct = default)
        => throw new InvalidOperationException("WorkOS invitations are not available in dev auth mode.");

    public Task<string> CreateOrganizationAsync(string name, CancellationToken ct = default)
        => throw new InvalidOperationException("WorkOS org creation is not available in dev auth mode.");

    public Task DeleteUserAsync(string workOsUserId, CancellationToken ct = default)
        => throw new InvalidOperationException("WorkOS user deletion is not available in dev auth mode.");
}
