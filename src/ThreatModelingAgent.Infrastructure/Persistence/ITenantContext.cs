using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence;

/// <summary>
/// Provides the current tenant's OrgId extracted from the validated JWT.
/// Implemented by the API's TenantContext which reads from HttpContext.
/// The Worker uses a scoped implementation populated from Service Bus message metadata.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// OrgId from the validated JWT claim. Null for platform-level (unauthenticated or system) contexts.
    /// When null, RLS will deny all tenant-scoped rows — fail-secure by design.
    /// </summary>
    OrgId? CurrentOrgId { get; }
}
