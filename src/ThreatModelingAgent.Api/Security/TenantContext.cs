using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Infrastructure.Persistence;

namespace ThreatModelingAgent.Api.Security;

/// <summary>
/// Scoped per-request implementation of ITenantContext.
/// OrgId is extracted from the validated JWT 'org_id' claim and set once
/// per request by TenantContextMiddleware. It is never read from request body,
/// query string, or headers (CLAUDE.md §8.2).
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private OrgId? _orgId;

    public OrgId? CurrentOrgId => _orgId;

    /// <summary>
    /// Called exactly once per request by TenantContextMiddleware after JWT validation.
    /// </summary>
    public void SetFromClaim(Guid orgId)
    {
        if (_orgId.HasValue)
            throw new InvalidOperationException("TenantContext has already been set for this request.");
        _orgId = OrgId.From(orgId);
    }
}
