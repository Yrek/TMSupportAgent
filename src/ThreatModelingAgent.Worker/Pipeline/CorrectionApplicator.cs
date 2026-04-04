using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Worker.Pipeline.Contracts;

namespace ThreatModelingAgent.Worker.Pipeline;

/// <summary>
/// Applies user corrections from <see cref="ArchitectureCorrection"/> records to an in-memory
/// <see cref="CanonicalModel"/> before re-analysis begins.
///
/// Called in Phase 2 when the user has confirmed an architecture that contains corrections.
///
/// SECURITY:
/// - Corrections are already validated and written by the API; we treat them as trusted
///   internal data (org-scoped, RLS-protected). No re-validation of individual field values.
/// - Corrections for unknown element IDs are logged and skipped — never crash the pipeline.
/// - No correction data is placed in LLM prompts. Corrections modify the canonical model
///   in deterministic code; the model is then handed to the LLM as-is. (CLAUDE.md §16.2)
/// </summary>
internal static class CorrectionApplicator
{
    /// <summary>
    /// Returns a new <see cref="CanonicalModel"/> with all corrections applied in created_at order.
    /// The original model is not mutated — CanonicalModel is an immutable record.
    /// </summary>
    public static CanonicalModel Apply(
        CanonicalModel model,
        IReadOnlyList<ArchitectureElement> elements,
        IReadOnlyList<ArchitectureCorrection> corrections,
        ILogger logger)
    {
        if (corrections.Count == 0) return model;

        // Build a map from element DB ID → element Name so we can find items in the canonical
        // model (which uses string labels, not GUIDs) from correction.ElementId
        var idToName = elements.ToDictionary(e => e.Id, e => e.Name);
        var idToType = elements.ToDictionary(e => e.Id, e => e.ElementType);

        // Track names of elements removed so downstream corrections targeting them are skipped
        var removedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Apply ordered — corrections are ordered by created_at ascending in the repository
        foreach (var correction in corrections)
        {
            model = correction.CorrectionType switch
            {
                CorrectionType.Update       => ApplyUpdate(model, correction, idToName, removedNames, logger),
                CorrectionType.MarkIncorrect => ApplyRemoval(model, correction, idToName, idToType, removedNames, logger),
                // MarkAssumed / MarkConfirmed / AddNote affect metadata only, not the canonical model
                _ => model
            };
        }

        return model;
    }

    // ── Field update ──────────────────────────────────────────────────────────

    private static CanonicalModel ApplyUpdate(
        CanonicalModel model,
        ArchitectureCorrection correction,
        Dictionary<Guid, string> idToName,
        HashSet<string> removedNames,
        ILogger logger)
    {
        if (correction.ElementId is null || correction.FieldName is null || correction.CorrectedValue is null)
            return model;

        if (!idToName.TryGetValue(correction.ElementId.Value, out var originalName))
        {
            logger.LogDebug("CorrectionApplicator: unknown ElementId {Id}, skipping", correction.ElementId);
            return model;
        }

        if (removedNames.Contains(originalName)) return model;

        var field = correction.FieldName.ToLowerInvariant();
        var newValue = correction.CorrectedValue;

        return field switch
        {
            "name" or "label" => RenameElement(model, originalName, newValue, idToName, correction.ElementId.Value),
            "description"     => UpdateDescription(model, originalName, newValue),
            _ => model  // Unknown field — skip silently
        };
    }

    private static CanonicalModel RenameElement(
        CanonicalModel model, string oldName, string newName,
        Dictionary<Guid, string> idToName, Guid elementId)
    {
        // Update idToName so subsequent corrections use the new name
        idToName[elementId] = newName;

        return model with
        {
            Components     = model.Components.Select(c => c.Label == oldName ? c with { Label = newName } : c).ToArray(),
            Actors         = model.Actors.Select(a => a.Label == oldName ? a with { Label = newName } : a).ToArray(),
            ExternalSystems = model.ExternalSystems.Select(e => e.Label == oldName ? e with { Label = newName } : e).ToArray(),
            DataStores     = model.DataStores.Select(d => d.Label == oldName ? d with { Label = newName } : d).ToArray(),
            TrustBoundaries = model.TrustBoundaries.Select(tb => tb.Label == oldName ? tb with { Label = newName } : tb).ToArray(),
            BackgroundJobs = model.BackgroundJobs.Select(b => b.Label == oldName ? b with { Label = newName } : b).ToArray(),
            AiLlmBoundaries = model.AiLlmBoundaries.Select(a => a.Label == oldName ? a with { Label = newName } : a).ToArray(),
        };
    }

    private static CanonicalModel UpdateDescription(CanonicalModel model, string name, string description)
        => model with
        {
            Components = model.Components
                .Select(c => c.Label == name ? c with { Description = description } : c)
                .ToArray()
            // Description only applies to Components in the canonical contract
        };

    // ── Element removal ───────────────────────────────────────────────────────

    private static CanonicalModel ApplyRemoval(
        CanonicalModel model,
        ArchitectureCorrection correction,
        Dictionary<Guid, string> idToName,
        Dictionary<Guid, ElementType> idToType,
        HashSet<string> removedNames,
        ILogger logger)
    {
        if (correction.ElementId is null) return model;

        if (!idToName.TryGetValue(correction.ElementId.Value, out var name))
        {
            logger.LogDebug("CorrectionApplicator: unknown ElementId {Id} for removal, skipping", correction.ElementId);
            return model;
        }

        removedNames.Add(name);

        if (!idToType.TryGetValue(correction.ElementId.Value, out var elementType))
            return model;

        return elementType switch
        {
            ElementType.Component      => model with { Components      = model.Components.Where(c => c.Label != name).ToArray() },
            ElementType.Actor          => model with { Actors          = model.Actors.Where(a => a.Label != name).ToArray() },
            ElementType.ExternalSystem => model with { ExternalSystems = model.ExternalSystems.Where(e => e.Label != name).ToArray() },
            ElementType.DataStore      => model with { DataStores      = model.DataStores.Where(d => d.Label != name).ToArray() },
            ElementType.DataFlow       => model with
            {
                DataFlows  = model.DataFlows.Where(f => $"{f.From} → {f.To}" != name && f.Label != name).ToArray(),
                AsyncFlows = model.AsyncFlows.Where(f => $"async: {f.From} → {f.To}" != name && f.Label != name).ToArray()
            },
            ElementType.TrustBoundary  => model with { TrustBoundaries = model.TrustBoundaries.Where(tb => tb.Label != name).ToArray() },
            ElementType.BackgroundJob  => model with { BackgroundJobs  = model.BackgroundJobs.Where(b => b.Label != name).ToArray() },
            ElementType.LlmBoundary    => model with { AiLlmBoundaries = model.AiLlmBoundaries.Where(a => a.Label != name).ToArray() },
            _ => model
        };
    }
}
