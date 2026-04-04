using ThreatModelingAgent.Domain.Entities;

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
    bool IsConfirmed,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ArchitectureElementDto> Elements)
{
    public static ArchitectureDto From(Architecture arch, IReadOnlyList<ArchitectureElement> elements)
    {
        return new ArchitectureDto(
            Id: arch.Id,
            JobId: arch.JobId.Value,
            Version: arch.Version,
            Classification: arch.Classification,
            SystemPurpose: arch.SystemPurpose,
            Assumptions: DeserializeJsonb(arch.AssumptionsJson),
            Gaps: DeserializeJsonb(arch.GapsJson),
            ClarificationQuestions: DeserializeJsonb(arch.ClarificationQuestionsJson),
            IsConfirmed: arch.IsConfirmed,
            ConfirmedAt: arch.ConfirmedAt,
            CreatedAt: arch.CreatedAt,
            UpdatedAt: arch.UpdatedAt,
            Elements: elements.Select(ArchitectureElementDto.From).ToList());
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
    DateTimeOffset CreatedAt)
{
    public static ArchitectureElementDto From(ArchitectureElement el)
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
            CreatedAt: el.CreatedAt);
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Confirm architecture and optionally note any user acknowledgements.
/// An empty body is valid — confirmation alone is sufficient to proceed.
/// </summary>
public class ConfirmArchitectureRequest
{
    public string? Note { get; set; }
}

/// <summary>
/// Patch an architecture element's name, description, or properties.
/// All fields are optional — only non-null values are applied.
/// </summary>
public class PatchElementRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    // PropertiesJson intentionally not exposed — clients patch through Name/Description only
}
