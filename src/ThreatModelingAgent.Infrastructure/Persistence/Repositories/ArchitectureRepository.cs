using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Repositories;

internal sealed class ArchitectureRepository(AppDbContext db) : IArchitectureRepository
{
    public Task<Architecture?> GetByJobIdAsync(JobId jobId, OrgId orgId, CancellationToken ct = default)
        // org_id predicate is defence-in-depth alongside RLS (CLAUDE.md §8.2 BOLA)
        => db.Architectures
            .Include(a => a.Elements)
            .Include(a => a.Corrections)
            .FirstOrDefaultAsync(a => a.JobId == jobId && a.OrgId == orgId, ct);

    public async Task AddAsync(Architecture architecture, CancellationToken ct = default)
        => await db.Architectures.AddAsync(architecture, ct);

    public async Task AddElementAsync(ArchitectureElement element, CancellationToken ct = default)
        => await db.ArchitectureElements.AddAsync(element, ct);

    public async Task AddCorrectionAsync(ArchitectureCorrection correction, CancellationToken ct = default)
        => await db.ArchitectureCorrections.AddAsync(correction, ct);

    public Task<ArchitectureElement?> GetElementByIdAsync(Guid elementId, OrgId orgId, CancellationToken ct = default)
        => db.ArchitectureElements
            .FirstOrDefaultAsync(e => e.Id == elementId && e.OrgId == orgId, ct);

    public async Task<IReadOnlyList<ArchitectureElement>> ListElementsAsync(
        Guid architectureId, OrgId orgId, CancellationToken ct = default)
        => await db.ArchitectureElements
            .Where(e => e.ArchitectureId == architectureId && e.OrgId == orgId)
            .OrderBy(e => e.ElementType)
            .ThenBy(e => e.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ArchitectureCorrection>> ListCorrectionsAsync(
        Guid architectureId, OrgId orgId, CancellationToken ct = default)
        => await db.ArchitectureCorrections
            .Where(c => c.ArchitectureId == architectureId && c.OrgId == orgId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
