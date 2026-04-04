using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

/// <summary>
/// A single component, actor, flow, trust boundary, etc. within the canonical architecture model.
/// Spec: data-model §4.7.
/// </summary>
public class ArchitectureElement
{
    public Guid Id { get; private set; }
    public Guid ArchitectureId { get; private set; }
    public OrgId OrgId { get; private set; }
    public ElementType ElementType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string PropertiesJson { get; private set; } = "{}"; // jsonb: trust_zone, auth_mechanism, etc.
    public string Source { get; private set; } = string.Empty; // extracted | user_added
    public ConfidenceLevel? ExtractionConfidence { get; private set; } // null for user_added
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    public ICollection<ArchitectureCorrection> Corrections { get; private set; } = [];

    private ArchitectureElement() { }

    public static ArchitectureElement CreateExtracted(
        Guid architectureId,
        OrgId orgId,
        ElementType elementType,
        string name,
        string? description,
        string propertiesJson,
        ConfidenceLevel extractionConfidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 255) throw new ArgumentException("Name exceeds 255 chars.", nameof(name));

        var now = DateTimeOffset.UtcNow;
        return new ArchitectureElement
        {
            Id = Guid.NewGuid(),
            ArchitectureId = architectureId,
            OrgId = orgId,
            ElementType = elementType,
            Name = name,
            Description = description,
            PropertiesJson = propertiesJson,
            Source = "extracted",
            ExtractionConfidence = extractionConfidence,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static ArchitectureElement CreateUserAdded(
        Guid architectureId,
        OrgId orgId,
        ElementType elementType,
        string name,
        string? description,
        string propertiesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 255) throw new ArgumentException("Name exceeds 255 chars.", nameof(name));

        var now = DateTimeOffset.UtcNow;
        return new ArchitectureElement
        {
            Id = Guid.NewGuid(),
            ArchitectureId = architectureId,
            OrgId = orgId,
            ElementType = elementType,
            Name = name,
            Description = description,
            PropertiesJson = propertiesJson,
            Source = "user_added",
            ExtractionConfidence = null,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string? name, string? description, string? propertiesJson)
    {
        if (name is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (name.Length > 255) throw new ArgumentException("Name exceeds 255 chars.", nameof(name));
            Name = name;
        }

        if (description is not null)
            Description = description;

        if (propertiesJson is not null)
            PropertiesJson = propertiesJson;

        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
