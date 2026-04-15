using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Repositories;

internal sealed class IdpConfigRepository(AppDbContext db) : IIdpConfigRepository
{
    // org_id predicate is defence-in-depth alongside RLS (CLAUDE.md §8.2 BOLA)
    public Task<OrgIdpConfig?> GetByOrgAsync(OrgId orgId, CancellationToken ct = default)
        => db.OrgIdpConfigs.FirstOrDefaultAsync(c => c.OrgId == orgId, ct);

    public Task<bool> DomainHintInUseByAnotherOrgAsync(OrgId orgId, string normalizedDomainHint, CancellationToken ct = default)
        => db.OrgIdpConfigs
            .AnyAsync(c => c.OrgId != orgId && c.DomainHints.Contains(normalizedDomainHint), ct);

    public async Task AddAsync(OrgIdpConfig config, CancellationToken ct = default)
        => await db.OrgIdpConfigs.AddAsync(config, ct);

    public void Remove(OrgIdpConfig config)
        => db.OrgIdpConfigs.Remove(config);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
