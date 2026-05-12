using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(OrgId id, CancellationToken ct = default);
    Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Organization?> GetByWorkOsOrgIdAsync(string workOsOrgId, CancellationToken ct = default);
    Task<Organization?> GetByEntraTenantIdAsync(string entraTenantId, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Organization>> ListByUserAsync(UserId userId, CancellationToken ct = default);
    Task AddAsync(Organization organization, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
