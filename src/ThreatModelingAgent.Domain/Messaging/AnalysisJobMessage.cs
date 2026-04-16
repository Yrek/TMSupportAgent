namespace ThreatModelingAgent.Domain.Messaging;

/// <summary>
/// Message envelope placed on the Service Bus queue.
///
/// Phase 1 (Parse): sent by API when a job is submitted.
/// Phase 2 (Analyze): sent by API when user confirms the canonical model.
///
/// Contains only identifiers and optional user-supplied context text.
/// org_id is validated by the worker against the job record before processing.
/// </summary>
public sealed record AnalysisJobMessage(
    Guid JobId,
    Guid OrgId,
    string ArtifactBlobPath,
    string ArtifactType,
    string? ApplicationDescription = null,
    string? ArchitectureDescription = null,
    string[]? SelectedMethods = null,
    PipelinePhase Phase = PipelinePhase.Parse);

public enum PipelinePhase
{
    Parse,
    Analyze
}
