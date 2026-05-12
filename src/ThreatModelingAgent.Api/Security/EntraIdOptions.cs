namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Runtime configuration for Entra ID authentication mode.
/// Registered as singleton in DI; populated from the "EntraId" config section at startup.
///
/// Self-hosted (1 Entra tenant per deployment):
///   - DefaultOrgId is set; all Entra users land in that org.
///   - EntraTenantId on the Organization entity is not required.
///
/// SaaS per-org path (future):
///   - DefaultOrgId is null; org is resolved by matching the JWT "tid" claim against
///     Organization.EntraTenantId via IOrganizationRepository.GetByEntraTenantIdAsync.
///   - Requires each org to have EntraTenantId populated by the platform admin.
/// </summary>
public sealed class EntraIdOptions
{
    public bool Enabled { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Self-hosted: the single org GUID for this deployment.
    /// All authenticated Entra users are provisioned into this org.
    /// Null = SaaS mode; resolve org dynamically from "tid" claim.
    /// </summary>
    public Guid? DefaultOrgId { get; init; }

    /// <summary>
    /// Entra object IDs (oid) that receive OrgMemberRole.Owner on JIT provisioning.
    /// Empty = all JIT-provisioned users receive Member.
    /// </summary>
    public HashSet<string> AdminOids { get; init; } = [];
}
