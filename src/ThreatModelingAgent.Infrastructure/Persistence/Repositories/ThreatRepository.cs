using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Repositories;

internal sealed class ThreatRepository(AppDbContext db) : IThreatRepository
{
    public Task<Threat?> GetByIdAsync(Guid threatId, OrgId orgId, CancellationToken ct = default)
        => db.Threats
            .Include(t => t.Mitigations)
            .Include(t => t.FrameworkMappings)
            .Include(t => t.Notes)
            .FirstOrDefaultAsync(t => t.Id == threatId && t.OrgId == orgId, ct);

    public async Task<IReadOnlyList<Threat>> ListByJobAsync(JobId jobId, OrgId orgId, Guid? elementId = null, CancellationToken ct = default)
        => await db.Threats
            .Include(t => t.Mitigations)
            .Include(t => t.FrameworkMappings)
            .Where(t => t.JobId == jobId && t.OrgId == orgId)
            // GAP-TH3: filter by element when requested (Npgsql translates Contains → ANY())
            .Where(t => elementId == null || t.AffectedElementIds.Contains(elementId.Value))
            .OrderBy(t => t.Identifier)
            .ToListAsync(ct);

    public Task<int> CountByJobAsync(JobId jobId, OrgId orgId, CancellationToken ct = default)
        => db.Threats.CountAsync(t => t.JobId == jobId && t.OrgId == orgId, ct);

    public async Task AddAsync(Threat threat, CancellationToken ct = default)
        => await db.Threats.AddAsync(threat, ct);

    public async Task AddMitigationAsync(Mitigation mitigation, CancellationToken ct = default)
        => await db.Mitigations.AddAsync(mitigation, ct);

    public async Task AddFrameworkMappingAsync(FrameworkMapping mapping, CancellationToken ct = default)
        => await db.FrameworkMappings.AddAsync(mapping, ct);

    public async Task AddRejectedCandidateAsync(RejectedCandidate candidate, CancellationToken ct = default)
        => await db.RejectedCandidates.AddAsync(candidate, ct);

    public async Task AddNoteAsync(ThreatNote note, CancellationToken ct = default)
        => await db.ThreatNotes.AddAsync(note, ct);

    public async Task<string> NextIdentifierAsync(JobId jobId, OrgId orgId, CancellationToken ct = default)
    {
        var count = await CountByJobAsync(jobId, orgId, ct);
        return $"T-{count + 1:D3}";
    }

    public async Task DeleteSystemGeneratedAsync(JobId jobId, OrgId orgId, CancellationToken ct = default)
    {
        // Bulk delete system threats; EF cascade removes child mitigations, mappings, etc.
        // org_id predicate is defence-in-depth alongside RLS (CLAUDE.md §8.2)
        var systemThreats = await db.Threats
            .Where(t => t.JobId == jobId && t.OrgId == orgId && t.Source == "system")
            .ToListAsync(ct);

        db.Threats.RemoveRange(systemThreats);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
