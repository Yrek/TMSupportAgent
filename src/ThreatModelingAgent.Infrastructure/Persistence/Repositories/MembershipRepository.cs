using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Repositories;

internal sealed class MembershipRepository(AppDbContext db) : IMembershipRepository
{
    public Task<OrgMembership?> GetAsync(OrgId orgId, UserId userId, CancellationToken ct = default)
        => db.OrgMemberships.FirstOrDefaultAsync(m => m.OrgId == orgId && m.UserId == userId, ct);

    public async Task<IReadOnlyList<OrgMembership>> ListByOrgAsync(OrgId orgId, CancellationToken ct = default)
        => await db.OrgMemberships
            .Where(m => m.OrgId == orgId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OrgMembership>> ListByUserAsync(UserId userId, CancellationToken ct = default)
        => await db.OrgMemberships
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

    public async Task AddAsync(OrgMembership membership, CancellationToken ct = default)
        => await db.OrgMemberships.AddAsync(membership, ct);

    public Task RemoveAsync(OrgMembership membership, CancellationToken ct = default)
    {
        db.OrgMemberships.Remove(membership);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
