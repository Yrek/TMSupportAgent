using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Infrastructure.Persistence;

namespace ThreatModelingAgent.Worker;

/// <summary>
/// ITenantContext implementation for the Worker service.
/// Set from validated Service Bus message metadata before any DB queries.
/// </summary>
public sealed class WorkerTenantContext : ITenantContext
{
    private OrgId? _orgId;
    public OrgId? CurrentOrgId => _orgId;

    public void Set(OrgId orgId) => _orgId = orgId;
}
