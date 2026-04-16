using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
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

    public async Task<IReadOnlyList<Threat>> ListByJobAsync(
        JobId jobId,
        OrgId orgId,
        Guid? elementId = null,
        FindingType[]? findingTypes = null,
        ThreatStatus[]? statuses = null,
        ConfidenceLevel[]? confidences = null,
        string[]? methodCategories = null,
        string[]? frameworks = null,
        CancellationToken ct = default)
    {
        var query = db.Threats
            .Include(t => t.Mitigations)
            .Include(t => t.FrameworkMappings)
            .Where(t => t.JobId == jobId && t.OrgId == orgId)
            // GAP-TH3: filter by element when requested (Npgsql translates Contains → ANY())
            .Where(t => elementId == null || t.AffectedElementIds.Contains(elementId.Value));

        if (findingTypes is { Length: > 0 })
            query = query.Where(t => findingTypes.Contains(t.FindingType));

        if (statuses is { Length: > 0 })
            query = query.Where(t => statuses.Contains(t.Status));

        if (confidences is { Length: > 0 })
            query = query.Where(t => confidences.Contains(t.Confidence));

        if (methodCategories is { Length: > 0 })
            query = query.Where(t => methodCategories.Contains(t.MethodCategory));

        if (frameworks is { Length: > 0 })
            query = query.Where(t => t.FrameworkMappings.Any(fm => frameworks.Contains(fm.Framework)));

        return await query
            .OrderBy(t => t.Identifier)
            .ToListAsync(ct);
    }

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
        // Use max numeric suffix, not count+1, so numbering remains monotonic even
        // when threats are deleted (avoids accidental identifier reuse).
        var identifiers = await db.Threats
            .Where(t => t.JobId == jobId && t.OrgId == orgId)
            .Select(t => t.Identifier)
            .ToListAsync(ct);

        var max = 0;
        foreach (var identifier in identifiers)
        {
            if (identifier is { Length: > 2 } &&
                identifier.StartsWith("T-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(identifier[2..], out var parsed) &&
                parsed > max)
            {
                max = parsed;
            }
        }

        return $"T-{max + 1:D3}";
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
