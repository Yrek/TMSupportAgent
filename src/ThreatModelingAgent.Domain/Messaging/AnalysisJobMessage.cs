namespace ThreatModelingAgent.Domain.Messaging;

/// <summary>
/// Message envelope placed on the Service Bus queue.
///
/// Phase 1 (Parse): sent by API when a job is submitted.
/// Phase 2 (Analyze): sent by API when user confirms the canonical model.
///
/// Contains only identifiers — no tenant architecture data, no PII (CLAUDE.md §16.3).
/// org_id is validated by the worker against the job record before processing.
/// </summary>
public sealed record AnalysisJobMessage(
    Guid JobId,
    Guid OrgId,
    string ArtifactBlobPath,
    string ArtifactType,
    PipelinePhase Phase = PipelinePhase.Parse);

public enum PipelinePhase
{
    Parse,   // DETECT → PARSE → NORMALIZE → AWAITING_REVIEW
    Analyze  // CLASSIFY → ANALYZE → SYNTHESIZE → COMPLETE
}
