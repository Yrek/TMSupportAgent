using System.Text.Json;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;

namespace ThreatModelingAgent.Api.Dtos;

// ── Response DTOs (CLAUDE.md §6.6 — purpose-specific, no domain model exposed) ──

public record ArchitectureDto(
    Guid Id,
    Guid JobId,
    int Version,
    string[] Classification,
    string? SystemPurpose,
    object Assumptions,       // deserialized from jsonb
    object Gaps,
    object ClarificationQuestions,
    object ClarificationAnswers,  // previously submitted answers; deserialized from jsonb
    object? DeploymentContext,  // deserialized from jsonb; null means not yet detected
    bool IsConfirmed,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ArchitectureElementDto> Elements)
{
    public static ArchitectureDto From(
        Architecture arch,
        IReadOnlyList<ArchitectureElement> elements,
        IReadOnlyList<ArchitectureCorrection>? corrections = null)
    {
        // Group corrections by element ID for O(1) lookup; skip orphaned corrections (ElementId == null)
        var correctionsByElement = (corrections ?? [])
            .Where(c => c.ElementId.HasValue)
            .GroupBy(c => c.ElementId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ArchitectureCorrection>)g.ToList());

        return new ArchitectureDto(
            Id: arch.Id,
            JobId: arch.JobId.Value,
            Version: arch.Version,
            Classification: arch.Classification,
            SystemPurpose: arch.SystemPurpose,
            Assumptions: DeserializeJsonb(arch.AssumptionsJson),
            Gaps: DeserializeJsonb(arch.GapsJson),
            ClarificationQuestions: DeserializeJsonb(arch.ClarificationQuestionsJson),
            ClarificationAnswers: DeserializeJsonb(arch.ClarificationAnswersJson),
            DeploymentContext: arch.DeploymentContextJson == "{}" ? null : DeserializeJsonb(arch.DeploymentContextJson),
            IsConfirmed: arch.IsConfirmed,
            ConfirmedAt: arch.ConfirmedAt,
            CreatedAt: arch.CreatedAt,
            UpdatedAt: arch.UpdatedAt,
            Elements: elements.Select(el =>
                ArchitectureElementDto.From(el,
                    correctionsByElement.GetValueOrDefault(el.Id))).ToList());
    }

    private static object DeserializeJsonb(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<object>(json)!;
        }
        catch
        {
            return Array.Empty<object>();
        }
    }
}

public record ArchitectureElementDto(
    Guid Id,
    string ElementType,
    string Name,
    string? Description,
    object Properties,
    string Source,
    string? ExtractionConfidence,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CorrectionDto> Corrections)
{
    public static ArchitectureElementDto From(ArchitectureElement el,
        IReadOnlyList<ArchitectureCorrection>? corrections = null)
    {
        object props;
        try { props = System.Text.Json.JsonSerializer.Deserialize<object>(el.PropertiesJson)!; }
        catch { props = new { }; }

        return new ArchitectureElementDto(
            Id: el.Id,
            ElementType: el.ElementType.ToString(),
            Name: el.Name,
            Description: el.Description,
            Properties: props,
            Source: el.Source,
            ExtractionConfidence: el.ExtractionConfidence?.ToString(),
            CreatedAt: el.CreatedAt,
            Corrections: (corrections ?? []).Select(CorrectionDto.From).ToList());
    }
}

public record CorrectionDto(
    Guid Id,
    string CorrectionType,
    string? FieldName,
    string? OriginalValue,
    string? CorrectedValue,
    string? Note,
    Guid CorrectedBy,
    DateTimeOffset CreatedAt)
{
    public static CorrectionDto From(ArchitectureCorrection c) => new(
        Id: c.Id,
        CorrectionType: c.CorrectionType.ToString(),
        FieldName: c.FieldName,
        OriginalValue: c.OriginalValue,
        CorrectedValue: c.CorrectedValue,
        Note: c.Note,
        CorrectedBy: c.CorrectedBy.Value,
        CreatedAt: c.CreatedAt);
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Confirm architecture and optionally note any user acknowledgements.
/// An empty body is valid — confirmation alone is sufficient to proceed.
/// </summary>
public class ConfirmArchitectureRequest
{
    public string? Note { get; set; }
    public string[]? SelectedMethods { get; set; }
    public string[]? RejectedMethods { get; set; }
    public ClarificationAnswer[]? ClarificationAnswers { get; set; }
}

public class ClarificationAnswer
{
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Priority { get; set; } = "";
}

/// <summary>
/// Patch an architecture element's name, description, or properties.
/// All fields are optional — only non-null values are applied.
/// </summary>
public class PatchElementRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Optional free-form properties object. Replaces the existing properties when provided.
    /// Well-known keys: port, protocol, auth, trustZone, technology, encryption.
    /// Any additional key-value pairs are accepted and preserved.
    /// Example: { "port": 443, "protocol": "https", "auth": "jwt", "trustZone": "internal" }
    /// </summary>
    public JsonElement? Properties { get; set; }
}

/// <summary>
/// Record a correction or annotation on an extracted architecture element.
/// Corrections are immutable once written — they form a provenance trail.
/// Valid correctionTypes: Update, MarkIncorrect, MarkAssumed, MarkConfirmed, AddNote.
/// </summary>
public class CorrectElementRequest
{
    /// <summary>Must be a valid CorrectionType enum value (case-insensitive).</summary>
    public string CorrectionType { get; set; } = null!;

    /// <summary>Field being corrected — required for Update corrections.</summary>
    public string? FieldName { get; set; }

    /// <summary>The original (extracted) value — optional, for provenance.</summary>
    public string? OriginalValue { get; set; }

    /// <summary>The corrected value — required for Update corrections.</summary>
    public string? CorrectedValue { get; set; }

    /// <summary>Free-text note — required for AddNote, optional otherwise.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Correct the auto-detected deployment context before confirming the architecture.
/// All fields are required — a full replacement, not a partial patch.
/// Valid environments: aws, azure, gcp, on_prem, hybrid, unknown.
/// Valid infraControls: waf, cdn, api_gateway, load_balancer, ddos_protection.
/// </summary>
public class PatchDeploymentContextRequest
{
    public string Environment { get; set; } = null!;
    public bool Containerized { get; set; }
    public bool Serverless { get; set; }
    public string[] InfraControls { get; set; } = [];
}

/// <summary>
/// Add a new element to an architecture manually.
/// Valid element types: Component, Actor, DataFlow, TrustBoundary, DataStore,
/// ExternalSystem, Identity, BackgroundJob, LlmBoundary.
/// </summary>
public class AddElementRequest
{
    /// <summary>Element type — must be a valid ElementType enum value (case-insensitive).</summary>
    public string ElementType { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>
    /// Optional free-form properties object.
    /// Well-known keys: port, protocol, auth, trustZone, technology, encryption.
    /// Example: { "port": 5432, "protocol": "tcp", "auth": "password", "technology": "PostgreSQL" }
    /// </summary>
    public JsonElement? Properties { get; set; }
}
