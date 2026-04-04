using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public interface IThreatRepository
{
    Task<Threat?> GetByIdAsync(Guid threatId, OrgId orgId, CancellationToken ct = default);
    Task<IReadOnlyList<Threat>> ListByJobAsync(JobId jobId, OrgId orgId, CancellationToken ct = default);
    Task<int> CountByJobAsync(JobId jobId, OrgId orgId, CancellationToken ct = default);
    Task AddAsync(Threat threat, CancellationToken ct = default);
    Task AddMitigationAsync(Mitigation mitigation, CancellationToken ct = default);
    Task AddFrameworkMappingAsync(FrameworkMapping mapping, CancellationToken ct = default);
    Task AddRejectedCandidateAsync(RejectedCandidate candidate, CancellationToken ct = default);
    Task AddNoteAsync(ThreatNote note, CancellationToken ct = default);

    /// <summary>Returns the next available T-NNN identifier for the given job.</summary>
    Task<string> NextIdentifierAsync(JobId jobId, OrgId orgId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all pipeline-generated threats (source = 'system') for a job.
    /// Called before persisting new threats on re-analysis. User-added threats are preserved.
    /// </summary>
    Task DeleteSystemGeneratedAsync(JobId jobId, OrgId orgId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
