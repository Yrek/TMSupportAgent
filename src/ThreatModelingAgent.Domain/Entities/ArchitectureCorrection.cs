using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Entities;

/// <summary>
/// Immutable provenance record of a user correction on an extracted architecture element.
/// Corrections are never updated or deleted — they form an audit trail.
/// Spec: data-model §4.8.
/// </summary>
public class ArchitectureCorrection
{
    public Guid Id { get; private set; }
    public Guid? ElementId { get; private set; }       // null if element was deleted
    public Guid ArchitectureId { get; private set; }
    public OrgId OrgId { get; private set; }
    public UserId CorrectedBy { get; private set; }
    public CorrectionType CorrectionType { get; private set; }
    public string? FieldName { get; private set; }
    public string? OriginalValue { get; private set; }
    public string? CorrectedValue { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    // No UpdatedAt — corrections are immutable once written (spec §4.8)

    private ArchitectureCorrection() { }

    public static ArchitectureCorrection Create(
        Guid? elementId,
        Guid architectureId,
        OrgId orgId,
        UserId correctedBy,
        CorrectionType correctionType,
        string? fieldName,
        string? originalValue,
        string? correctedValue,
        string? note)
    {
        return new ArchitectureCorrection
        {
            Id = Guid.NewGuid(),
            ElementId = elementId,
            ArchitectureId = architectureId,
            OrgId = orgId,
            CorrectedBy = correctedBy,
            CorrectionType = correctionType,
            FieldName = fieldName,
            OriginalValue = originalValue,
            CorrectedValue = correctedValue,
            Note = note,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
