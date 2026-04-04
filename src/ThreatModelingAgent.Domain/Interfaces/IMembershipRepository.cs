using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public interface IMembershipRepository
{
    Task<OrgMembership?> GetAsync(OrgId orgId, UserId userId, CancellationToken ct = default);
    Task<IReadOnlyList<OrgMembership>> ListByOrgAsync(OrgId orgId, CancellationToken ct = default);
    Task<IReadOnlyList<OrgMembership>> ListByUserAsync(UserId userId, CancellationToken ct = default);
    Task AddAsync(OrgMembership membership, CancellationToken ct = default);
    Task RemoveAsync(OrgMembership membership, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
