using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Repositories;

/// <summary>
/// Platform admin repository — bypasses org-scoped RLS (runs as admin role in Postgres).
/// Methods here query across all tenants. Only used by AdminController which enforces
/// the platform:admin authorization policy before any call reaches this repository.
/// </summary>
internal sealed class AdminRepository(AppDbContext db) : IAdminRepository
{
    public async Task<(IReadOnlyList<AdminOrgSummary> Items, int Total)> ListOrgsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        // IgnoreQueryFilters bypasses the soft-delete global filter so admin can see all orgs
        var query = db.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(o => o.Name.ToLower().Contains(term) || o.Slug.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);

        var orgs = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.Slug,
                o.IsSuspended,
                o.SuspendedAt,
                o.CreatedAt,
                MemberCount = db.OrgMemberships.Count(m => m.OrgId == o.Id),
                JobCount    = db.Jobs.Count(j => j.OrgId == o.Id)
            })
            .ToListAsync(ct);

        var items = orgs.Select(o => new AdminOrgSummary(
            o.Id, o.Name, o.Slug, o.IsSuspended, o.SuspendedAt, o.CreatedAt,
            o.MemberCount, o.JobCount)).ToList();

        return (items, total);
    }

    public async Task<AdminOrgSummary?> GetOrgSummaryAsync(OrgId orgId, CancellationToken ct = default)
    {
        var o = await db.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.Id == orgId && o.DeletedAt == null)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.Slug,
                o.IsSuspended,
                o.SuspendedAt,
                o.CreatedAt,
                MemberCount = db.OrgMemberships.Count(m => m.OrgId == o.Id),
                JobCount    = db.Jobs.Count(j => j.OrgId == o.Id)
            })
            .FirstOrDefaultAsync(ct);

        if (o is null) return null;
        return new AdminOrgSummary(o.Id, o.Name, o.Slug, o.IsSuspended, o.SuspendedAt,
            o.CreatedAt, o.MemberCount, o.JobCount);
    }

    public Task<Organization?> GetOrgAsync(OrgId orgId, CancellationToken ct = default)
        => db.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orgId && o.DeletedAt == null, ct);

    public async Task<AdminSystemStats> GetSystemStatsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        var totalOrgs     = await db.Organizations.IgnoreQueryFilters().CountAsync(o => o.DeletedAt == null, ct);
        var suspendedOrgs = await db.Organizations.IgnoreQueryFilters().CountAsync(o => o.DeletedAt == null && o.IsSuspended, ct);
        var totalUsers    = await db.Users.CountAsync(u => u.DeletedAt == null, ct);
        var totalJobs     = await db.Jobs.CountAsync(ct);
        var jobsLast30    = await db.Jobs.CountAsync(j => j.CreatedAt >= cutoff, ct);

        return new AdminSystemStats(
            TotalOrgs:      totalOrgs,
            ActiveOrgs:     totalOrgs - suspendedOrgs,
            SuspendedOrgs:  suspendedOrgs,
            TotalUsers:     totalUsers,
            TotalJobs:      totalJobs,
            JobsLast30Days: jobsLast30);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
