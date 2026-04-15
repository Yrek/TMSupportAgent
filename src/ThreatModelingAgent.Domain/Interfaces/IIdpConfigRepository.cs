using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public interface IIdpConfigRepository
{
    Task<OrgIdpConfig?> GetByOrgAsync(OrgId orgId, CancellationToken ct = default);
    Task<bool> DomainHintInUseByAnotherOrgAsync(OrgId orgId, string normalizedDomainHint, CancellationToken ct = default);
    Task AddAsync(OrgIdpConfig config, CancellationToken ct = default);
    void Remove(OrgIdpConfig config);
    Task SaveChangesAsync(CancellationToken ct = default);
}
