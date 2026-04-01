using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Repositories;

internal sealed class JobRepository(AppDbContext db) : IJobRepository
{
    public Task<Job?> GetByIdAsync(JobId id, OrgId orgId, CancellationToken ct = default)
        // org_id predicate is defence-in-depth alongside RLS (CLAUDE.md §8.2 BOLA)
        => db.Jobs.FirstOrDefaultAsync(j => j.Id == id && j.OrgId == orgId, ct);

    public async Task<(IReadOnlyList<Job> Items, bool HasMore)> ListAsync(
        OrgId orgId,
        JobStatus? status,
        int pageSize,
        Guid? afterId,
        CancellationToken ct = default)
    {
        // Cap at 100 per spec (CLAUDE.md §9.3)
        var clampedSize = Math.Min(pageSize, 100);

        var query = db.Jobs.Where(j => j.OrgId == orgId);

        if (status.HasValue)
            query = query.Where(j => j.Status == status.Value);

        if (afterId.HasValue)
        {
            var cursor = await db.Jobs
                .Where(j => j.Id == JobId.From(afterId.Value))
                .Select(j => j.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (cursor != default)
                query = query.Where(j => j.CreatedAt < cursor);
        }

        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Take(clampedSize + 1)
            .ToListAsync(ct);

        var hasMore = items.Count > clampedSize;
        return (items.Take(clampedSize).ToList(), hasMore);
    }

    public async Task AddAsync(Job job, CancellationToken ct = default)
        => await db.Jobs.AddAsync(job, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
