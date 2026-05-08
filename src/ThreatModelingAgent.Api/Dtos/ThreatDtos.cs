using System.Text.Json;
using ThreatModelingAgent.Domain.Entities;

namespace ThreatModelingAgent.Api.Dtos;

// ── Response DTOs (CLAUDE.md §6.6 — purpose-specific, no domain model exposed) ──

public record RiskRatingDto(
    string Likelihood,
    string Impact,
    string Severity,
    string? LikelihoodJustification,
    string? ImpactJustification);

public record ThreatDto(
    Guid Id,
    string Identifier,
    string Title,
    string MethodCategory,
    Guid[] AffectedElementIds,
    string Description,
    string AttackScenario,
    string? Preconditions,
    string[] ImpactedAssets,
    string? SecurityImpact,
    string? PrivacyImpact,
    string? ExistingControls,
    string? ControlGaps,
    string Confidence,
    string[] EvidenceBasis,
    string EvidenceStrength,
    string FindingType,
    string Status,
    string Source,
    RiskRatingDto? RiskRating,
    IReadOnlyList<MitigationDto> Mitigations,
    IReadOnlyList<FrameworkMappingDto> FrameworkMappings,
    DateTimeOffset CreatedAt)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static ThreatDto From(Threat t) => new(
        Id: t.Id,
        Identifier: t.Identifier,
        Title: t.Title,
        MethodCategory: t.MethodCategory,
        AffectedElementIds: t.AffectedElementIds,
        Description: t.Description,
        AttackScenario: t.AttackScenario,
        Preconditions: t.Preconditions,
        ImpactedAssets: t.ImpactedAssets,
        SecurityImpact: t.SecurityImpact,
        PrivacyImpact: t.PrivacyImpact,
        ExistingControls: t.ExistingControls,
        ControlGaps: t.ControlGaps,
        Confidence: t.Confidence.ToString(),
        EvidenceBasis: t.EvidenceBasis,
        EvidenceStrength: t.EvidenceStrength.ToString(),
        FindingType: t.FindingType.ToString(),
        Status: t.Status.ToString(),
        Source: t.Source,
        RiskRating: DeserializeRiskRating(t.RiskRatingJson),
        Mitigations: t.Mitigations.Select(MitigationDto.From).ToList(),
        FrameworkMappings: t.FrameworkMappings.Select(FrameworkMappingDto.From).ToList(),
        CreatedAt: t.CreatedAt);

    private static RiskRatingDto? DeserializeRiskRating(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<RiskRatingDto>(json, JsonOpts); }
        catch { return null; }
    }
}

public record MitigationDto(
    Guid Id,
    string Title,
    string Description,
    string Priority,
    string? Category,
    string[] AcceptanceCriteria)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static MitigationDto From(Mitigation m) => new(
        Id: m.Id,
        Title: m.Title,
        Description: m.Description,
        Priority: m.Priority,
        Category: m.Category,
        AcceptanceCriteria: DeserializeAcceptanceCriteria(m.AcceptanceCriteriaJson));

    private static string[] DeserializeAcceptanceCriteria(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json, JsonOpts) ?? []; }
        catch { return []; }
    }
}

public record FrameworkMappingDto(
    Guid Id,
    string Framework,
    string Reference,
    string MappingType)
{
    public static FrameworkMappingDto From(FrameworkMapping fm) => new(
        Id: fm.Id,
        Framework: fm.Framework,
        Reference: fm.Reference,
        MappingType: fm.MappingType);
}

public record RejectedCandidateDto(
    Guid Id,
    string Title,
    string? MethodCategory,
    string RejectionReason,
    string? RejectionNote,
    DateTimeOffset CreatedAt)
{
    public static RejectedCandidateDto From(RejectedCandidate rc) => new(
        Id: rc.Id,
        Title: rc.Title,
        MethodCategory: rc.MethodCategory,
        RejectionReason: rc.RejectionReason,
        RejectionNote: rc.RejectionNote,
        CreatedAt: rc.CreatedAt);
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// User-submitted threat — minimal required fields; more fields can be added later via notes.
/// </summary>
public class AddThreatRequest
{
    public string Title { get; set; } = string.Empty;
    public string MethodCategory { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AttackScenario { get; set; } = string.Empty;
    public Guid[] AffectedElementIds { get; set; } = [];
}

/// <summary>
/// Update the status of a threat (open → accepted_risk | mitigated | wont_fix | false_positive).
/// </summary>
public class PatchThreatStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

/// <summary>Add an immutable discussion note to a threat.</summary>
public class AddThreatNoteRequest
{
    public string Body { get; set; } = string.Empty;
}
