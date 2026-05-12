using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Repositories;

internal sealed class OrganizationRepository(AppDbContext db) : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(OrgId id, CancellationToken ct = default)
        => db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => db.Organizations.FirstOrDefaultAsync(o => o.Slug == slug, ct);

    public Task<Organization?> GetByWorkOsOrgIdAsync(string workOsOrgId, CancellationToken ct = default)
        => db.Organizations.FirstOrDefaultAsync(o => o.WorkOsOrgId == workOsOrgId, ct);

    public Task<Organization?> GetByEntraTenantIdAsync(string entraTenantId, CancellationToken ct = default)
        => db.Organizations.FirstOrDefaultAsync(o => o.EntraTenantId == entraTenantId, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default)
        => db.Organizations.AnyAsync(o => o.Slug == slug, ct);

    public async Task<IReadOnlyList<Organization>> ListByUserAsync(UserId userId, CancellationToken ct = default)
    {
        // Join through memberships — no cross-tenant leakage since memberships are user-scoped
        var orgIds = await db.OrgMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.OrgId)
            .ToListAsync(ct);

        return await db.Organizations
            .Where(o => orgIds.Contains(o.Id))
            .OrderBy(o => o.Name)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Organization organization, CancellationToken ct = default)
        => await db.Organizations.AddAsync(organization, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
