using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public interface IArchitectureRepository
{
    Task<Architecture?> GetByJobIdAsync(JobId jobId, OrgId orgId, CancellationToken ct = default);
    Task AddAsync(Architecture architecture, CancellationToken ct = default);
    Task AddElementAsync(ArchitectureElement element, CancellationToken ct = default);
    Task AddCorrectionAsync(ArchitectureCorrection correction, CancellationToken ct = default);
    Task<ArchitectureElement?> GetElementByIdAsync(Guid elementId, OrgId orgId, CancellationToken ct = default);
    Task<IReadOnlyList<ArchitectureElement>> ListElementsAsync(Guid architectureId, OrgId orgId, CancellationToken ct = default);
    Task<IReadOnlyList<ArchitectureCorrection>> ListCorrectionsAsync(Guid architectureId, OrgId orgId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
