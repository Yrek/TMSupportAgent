using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public record AdminOrgSummary(
    OrgId Id,
    string Name,
    string Slug,
    bool IsSuspended,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset CreatedAt,
    int MemberCount,
    int JobCount);

public record AdminSystemStats(
    int TotalOrgs,
    int ActiveOrgs,
    int SuspendedOrgs,
    int TotalUsers,
    int TotalJobs,
    int JobsLast30Days);

public interface IAdminRepository
{
    Task<(IReadOnlyList<AdminOrgSummary> Items, int Total)> ListOrgsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default);

    Task<AdminOrgSummary?> GetOrgSummaryAsync(OrgId orgId, CancellationToken ct = default);

    Task<Organization?> GetOrgAsync(OrgId orgId, CancellationToken ct = default);

    Task<AdminSystemStats> GetSystemStatsAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
