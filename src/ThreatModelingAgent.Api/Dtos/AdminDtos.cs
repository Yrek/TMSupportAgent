using ThreatModelingAgent.Domain.Interfaces;

namespace ThreatModelingAgent.Api.Dtos;

// ── Admin response DTOs (platform:admin only — CLAUDE.md §6.6) ──────────────

public record AdminOrgDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsSuspended,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset CreatedAt,
    int MemberCount,
    int JobCount)
{
    public static AdminOrgDto From(AdminOrgSummary s)
        => new(s.Id.Value, s.Name, s.Slug, s.IsSuspended, s.SuspendedAt,
               s.CreatedAt, s.MemberCount, s.JobCount);
}

public record AdminSystemStatsDto(
    int TotalOrgs,
    int ActiveOrgs,
    int SuspendedOrgs,
    int TotalUsers,
    int TotalJobs,
    int JobsLast30Days)
{
    public static AdminSystemStatsDto From(AdminSystemStats s)
        => new(s.TotalOrgs, s.ActiveOrgs, s.SuspendedOrgs,
               s.TotalUsers, s.TotalJobs, s.JobsLast30Days);
}
