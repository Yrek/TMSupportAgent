using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(JobId id, OrgId orgId, CancellationToken ct = default);
    Task<(IReadOnlyList<Job> Items, bool HasMore)> ListAsync(
        OrgId orgId,
        JobStatus? status,
        int pageSize,
        Guid? afterId,
        CancellationToken ct = default);
    Task AddAsync(Job job, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
